using HallRental.API.Auth;
using HallRental.API.Data;
using HallRental.API.Models;
using HallRental.API.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 0. СПОСТЕРЕЖУВАНІСТЬ (Application Insights через OpenTelemetry)
// ==========================================
// Вмикається лише тоді, коли задано рядок підключення (в Azure його підставляє Bicep).
// Локально та в тестах телеметрія не надсилається.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// ==========================================
// 1. СХОВИЩЕ ДАНИХ
// ==========================================
// Немає рядка підключення -> сховище в пам'яті (локально, тести).
// Є рядок підключення -> Azure SQL через EF Core.
var connectionString = builder.Configuration.GetConnectionString("HallRental");
var useSql = !string.IsNullOrWhiteSpace(connectionString);

var healthChecks = builder.Services.AddHealthChecks();

if (useSql)
{
    builder.Services.AddDbContext<HallRentalDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            // Serverless-база може "прокидатися" до хвилини — повторюємо тимчасові помилки
            sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

    builder.Services.AddScoped<IHallRepository, EfHallRepository>();

    // Перевірка БД лише для /health/ready (тег "ready"), щоб пінги App Service не будили базу
    healthChecks.AddDbContextCheck<HallRentalDbContext>(tags: ["ready"]);
}
else
{
    builder.Services.AddSingleton<IHallRepository, InMemoryHallRepository>();
}

builder.Services.AddScoped<IHallService, HallService>();

// ==========================================
// 2. JWT (ключ із конфігурації / Key Vault, а не з коду)
// ==========================================
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey не налаштовано або він коротший за 32 байти. " +
        "Локально — appsettings.Development.json, в Azure — секрет у Key Vault.");
}

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

// Swagger UI (зручно для демонстрації роботодавцю)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ==========================================
// 3. МІГРАЦІЇ БД ПІД ЧАС СТАРТУ (лише коли явно увімкнено)
// ==========================================
// Спрощення для демо-проєкту. У продакшні міграції краще запускати окремим кроком
// CI/CD (EF migration bundle), а не під час старту кожного екземпляра.
if (useSql && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HallRentalDbContext>();
    db.Database.Migrate();
}

// ==========================================
// 4. БЕЗПЕКА ТА ГЛОБАЛЬНА ОБРОБКА ПОМИЛОК
// ==========================================

// Примусове перенаправлення всього трафіку через безпечний протокол HTTPS
// (в App Service TLS завершується на фронтенді; ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
//  дозволяє застосунку правильно бачити, що запит прийшов по HTTPS)
app.UseHttpsRedirection();

// Глобальна обробка винятків (запобігає витоку технічного стек-трейсу)
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.ContentType = "application/json";

        var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        if (exceptionFeature != null)
        {
            context.Response.StatusCode = exceptionFeature.Error switch
            {
                KeyNotFoundException => StatusCodes.Status404NotFound,
                ArgumentException => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            };

            var errorResponse = new { error = exceptionFeature.Error.Message };
            await context.Response.WriteAsJsonAsync(errorResponse);
        }
    });
});

if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Підключаємо конвеєр автентифікації та авторизації
app.UseAuthentication();
app.UseAuthorization();

// Liveness: застосунок живий (без звернення до БД) — використовує App Service Health check
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness: застосунок + БД доступні — для ручної перевірки та smoke-тесту
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapDemoTokenEndpoint(jwt);

// ==========================================
// 5. МАРШРУТИЗАЦІЯ (MINIMAL APIS)
// ==========================================

var hallsApi = app.MapGroup("/api/v1/halls");

/// <summary>
/// Публічний пошук доступних залів (не потребує токена).
/// </summary>
hallsApi.MapGet("/", ([FromQuery] int minCapacity, [FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime, [FromServices] IHallService hallService) =>
{
    var halls = hallService.GetAvailable(minCapacity, startTime, endTime);
    return Results.Ok(halls);
})
.WithSummary("Пошук доступних залів")
.WithDescription("Доступно публічно без автентифікації.");

/// <summary>
/// Додавання нового залу — ТІЛЬКИ ДЛЯ АДМІНІСТРАТОРІВ.
/// </summary>
hallsApi.MapPost("/", ([FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var id = hallService.Add(hall);
    return Results.Created($"/api/v1/halls/{id}", new { Id = id });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Додати новий зал")
.WithDescription("Вимагає токена з роллю Admin.");

/// <summary>
/// Редагування залу — ТІЛЬКИ ДЛЯ АДМІНІСТРАТОРІВ.
/// Дозволяє змінити ціну оренди, місткість та список додаткових послуг.
/// УВАГА: Accessories повністю ЗАМІНЮЄТЬСЯ, а не доповнюється — щоб додати нову
/// послугу (наприклад, "Звук" за 700 грн), у тілі запиту потрібно передати
/// повний список послуг залу (наявні + нова), інакше старі буде втрачено.
/// </summary>
hallsApi.MapPut("/{id:guid}", (Guid id, [FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var updated = hallService.Update(id, hall);
    if (!updated) return Results.NotFound(new { error = "Зал не знайдено" });
    return Results.Ok(new { message = "Дані залу успішно оновлено" });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Оновити дані залу")
.WithDescription("Вимагає токена з роллю Admin. Accessories передається повним списком (заміна, не додавання).");

/// <summary>
/// Додавання послуги до залу — ТІЛЬКИ ДЛЯ АДМІНІСТРАТОРІВ.
/// На відміну від PUT /{id}, додає одну послугу, не зачіпаючи решту списку.
/// Якщо послуга з такою назвою вже існує — оновлює її ціну.
/// </summary>
hallsApi.MapPost("/{id:guid}/accessories", (Guid id, [FromBody] AccessoryDto accessory, [FromServices] IHallService hallService) =>
{
    var added = hallService.AddAccessory(id, accessory);
    if (!added) return Results.NotFound(new { error = "Зал не знайдено" });
    return Results.Ok(new { message = "Послугу успішно додано" });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Додати послугу до залу")
.WithDescription("Вимагає токена з роллю Admin. Додає одну послугу без заміни решти списку.");

/// <summary>
/// Видалення залу — ТІЛЬКИ ДЛЯ АДМІНІСТРАТОРІВ.
/// </summary>
hallsApi.MapDelete("{id:guid}", (Guid id, [FromServices] IHallService hallService) =>
{
    var deleted = hallService.Delete(id);
    if (!deleted) return Results.NotFound(new { error = "Зал не знайдено" });
    return Results.NoContent();
})
.RequireAuthorization("AdminOnly")
.WithSummary("Видалити зал")
.WithDescription("Вимагає токена з роллю Admin.");

/// <summary>
/// Оформлення бронювання — ДЛЯ ЗАРЕЄСТРОВАНИХ ЮЗЕРІВ (та адміністраторів).
/// </summary>
hallsApi.MapPost("/book", ([FromBody] BookingRequest request, [FromServices] IHallService hallService) =>
{
    var response = hallService.Book(request);
    return Results.Ok(response);
})
.RequireAuthorization()
.WithSummary("Забронювати зал")
.WithDescription("Вимагає наявності валідного JWT-токена.");

/// <summary>
/// Бізнес-аналітика та звітність — ТІЛЬКИ ДЛЯ АДМІНІСТРАТОРІВ.
/// </summary>
var analyticsApi = app.MapGroup("/api/v1/analytics").RequireAuthorization("AdminOnly");

analyticsApi.MapGet("/summary", ([FromServices] IHallService hallService) =>
{
    var summary = hallService.GetAnalyticsSummary();
    return Results.Ok(summary);
})
.WithSummary("Отримання зведеної аналітики")
.WithDescription("Вимагає токена з роллю Admin.");

app.Run();

public partial class Program { }