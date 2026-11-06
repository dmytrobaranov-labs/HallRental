using HallRental.API.Auth;
using HallRental.API.Data;
using HallRental.API.Domain.Errors;
using HallRental.API.ErrorHandling;
using HallRental.API.Models;
using HallRental.API.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// 0. OBSERVABILITY (Application Insights via OpenTelemetry)
// ==========================================
// Enabled only when a connection string is set (Bicep provides it in Azure),
// so no telemetry is sent locally or from tests.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

// ==========================================
// 1. DATA STORAGE
// ==========================================
// No connection string -> in-memory repository (local runs, tests).
// Connection string present -> Azure SQL via EF Core.
var connectionString = builder.Configuration.GetConnectionString("HallRental");
var useSql = !string.IsNullOrWhiteSpace(connectionString);

var healthChecks = builder.Services.AddHealthChecks();

if (useSql)
{
    builder.Services.AddDbContext<HallRentalDbContext>(options =>
        options.UseSqlServer(connectionString, sql =>
            // A serverless database can take up to a minute to resume, so transient errors are retried
            sql.EnableRetryOnFailure(maxRetryCount: 6, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null)));

    builder.Services.AddScoped<IHallRepository, EfHallRepository>();

    // Database check only for /health/ready (tag "ready") so App Service pings do not wake the database
    healthChecks.AddDbContextCheck<HallRentalDbContext>(tags: ["ready"]);
}
else
{
    builder.Services.AddSingleton<IHallRepository, InMemoryHallRepository>();
}

builder.Services.AddScoped<IHallService, HallService>();

// Every error response is RFC 7807 ProblemDetails; see ProblemDetailsExceptionHandler for the mapping.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// ==========================================
// 2. JWT (key from configuration / Key Vault, never from code)
// ==========================================
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is not configured or is shorter than 32 bytes. " +
        "Locally use appsettings.Development.json; in Azure use a Key Vault secret.");
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

// Swagger UI (handy for demoing the API)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ==========================================
// 3. DATABASE MIGRATIONS ON STARTUP (only when explicitly enabled)
// ==========================================
// A simplification for a demo project. In production, migrations should run as a separate
// CI/CD step (EF migration bundle) rather than on startup of every instance.
if (useSql && app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<HallRentalDbContext>();
    db.Database.Migrate();
}

// ==========================================
// 4. SECURITY AND GLOBAL ERROR HANDLING
// ==========================================

// Global exception handling: domain errors become 400/404/409, anything else a generic 500
// (no stack traces or internal messages leak to clients). Registered first so it also
// catches exceptions thrown by every middleware below.
app.UseExceptionHandler();

// Turns empty error responses (401/403/405, Minimal API binding failures in Production)
// into ProblemDetails too, via the IProblemDetailsService registered by AddProblemDetails.
app.UseStatusCodePages();

// App Service terminates TLS at the front end; ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
// lets the app see that the original request came over HTTPS, so redirection works correctly.
app.UseHttpsRedirection();

if (app.Configuration.GetValue("Swagger:Enabled", true))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

// Liveness: the app is up (no database call); used by the App Service health check
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

// Readiness: the app and the database are reachable; for manual checks and the smoke test
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

app.MapDemoTokenEndpoint(jwt);

// ==========================================
// 5. ROUTING (MINIMAL APIS)
// ==========================================

var hallsApi = app.MapGroup("/api/v1/halls");

// Same shape as the 404 produced by HallNotFoundException in the exception handler.
static IResult HallNotFound(Guid id) => Results.Problem(
    statusCode: StatusCodes.Status404NotFound,
    title: ProblemTitles.HallNotFound,
    detail: HallNotFoundException.CreateMessage(id));

hallsApi.MapGet("/", ([FromQuery] int minCapacity, [FromQuery] DateTime? startTime, [FromQuery] DateTime? endTime, [FromServices] IHallService hallService) =>
{
    var halls = hallService.GetAvailable(minCapacity, startTime, endTime);
    return Results.Ok(halls);
})
.WithSummary("Search available halls")
.WithDescription("Public, no authentication required.");

hallsApi.MapPost("/", ([FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var id = hallService.Add(hall);
    return Results.Created($"/api/v1/halls/{id}", new { Id = id });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Add a new hall")
.WithDescription("Requires a token with the Admin role.")
.ProducesProblem(StatusCodes.Status400BadRequest);

// Accessories are REPLACED, not merged: to add a service via PUT the client must send
// the hall's full accessory list (existing + new), otherwise the existing ones are lost.
// Use POST /{id}/accessories to add a single service.
hallsApi.MapPut("/{id:guid}", (Guid id, [FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var updated = hallService.Update(id, hall);
    if (!updated) return HallNotFound(id);
    return Results.Ok(new { message = "Hall updated successfully." });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Update a hall")
.WithDescription("Requires a token with the Admin role. Accessories must be sent as the full list (replaced, not merged).")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound);

// Unlike PUT /{id}, adds one service without touching the rest of the list.
// If a service with the same name already exists, its price is updated instead.
hallsApi.MapPost("/{id:guid}/accessories", (Guid id, [FromBody] AccessoryDto accessory, [FromServices] IHallService hallService) =>
{
    var added = hallService.AddAccessory(id, accessory);
    if (!added) return HallNotFound(id);
    return Results.Ok(new { message = "Accessory added successfully." });
})
.RequireAuthorization("AdminOnly")
.WithSummary("Add an accessory to a hall")
.WithDescription("Requires a token with the Admin role. Adds a single accessory without replacing the rest of the list.")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound);

hallsApi.MapDelete("{id:guid}", (Guid id, [FromServices] IHallService hallService) =>
{
    var deleted = hallService.Delete(id);
    if (!deleted) return HallNotFound(id);
    return Results.NoContent();
})
.RequireAuthorization("AdminOnly")
.WithSummary("Delete a hall")
.WithDescription("Requires a token with the Admin role.")
.ProducesProblem(StatusCodes.Status404NotFound);

hallsApi.MapPost("/book", ([FromBody] BookingRequest request, [FromServices] IHallService hallService) =>
{
    var response = hallService.Book(request);
    return Results.Ok(response);
})
.RequireAuthorization()
.WithSummary("Book a hall")
.WithDescription("Requires a valid JWT.")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status409Conflict);

var analyticsApi = app.MapGroup("/api/v1/analytics").RequireAuthorization("AdminOnly");

analyticsApi.MapGet("/summary", ([FromServices] IHallService hallService) =>
{
    var summary = hallService.GetAnalyticsSummary();
    return Results.Ok(summary);
})
.WithSummary("Get summary analytics")
.WithDescription("Requires a token with the Admin role.");

app.Run();

public partial class Program { }