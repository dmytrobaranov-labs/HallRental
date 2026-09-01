using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Реєструємо сервіси у контейнері залежностей (Dependency Injection)
builder.Services.AddSingleton<IHallService, HallService>();

// ==========================================
// НАЛАШТУВАННЯ ВАЛІДАЦІЇ JWT-ТОКЕНІВ
// ==========================================
var secretKey = "SuperSecretKey12345678901234567890";
var key = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "HallRentalAPI",
            ValidAudience = "HallRentalClient",
            IssuerSigningKey = new SymmetricSecurityKey(key),
            // Вказуємо, що роль у токені зберігається в полі "role"
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Політика для обмеження доступу тільки для адміністраторів
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));
});

var app = builder.Build();

// ==========================================
// 1. БЕЗПЕКА ТА ГЛОБАЛЬНА ОБРОБКА ПОМИЛОК
// ==========================================

// Примусове перенаправлення всього трафіку через безпечний протокол HTTPS
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

// Підключаємо конвеєр автентифікації та авторизації
app.UseAuthentication();
app.UseAuthorization();

// ==========================================
// 2. МАРШРУТИЗАЦІЯ (MINIMAL APIS)
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