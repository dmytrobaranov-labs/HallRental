using System.Reflection;
using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// Налаштування Swagger для читання XML-коментарів
builder.Services.AddSwaggerGen(options =>
{
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

builder.Services.AddSingleton<IHallService, HallService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

var hallsApi = app.MapGroup("/api/v1/halls");

// Використовуємо .WithSummary() та .WithDescription() для Minimal API ендпоінтів
hallsApi.MapPost("/", ([FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var id = hallService.AddHall(hall);
    return Results.Created($"/api/v1/halls/{id}", new { Message = "Конференц-зал успішно створено", Data = new { hall_id = id } });
})
.WithSummary("Створення нового конференц-залу")
.WithDescription("Додає новий зал із заданою місткістю, базовою ціною та переліком послуг до системи.");

hallsApi.MapGet("/", ([FromQuery] int minCapacity, [FromServices] IHallService hallService) =>
{
    var halls = hallService.GetAvailableHalls(minCapacity);
    return Results.Ok(halls);
})
.WithSummary("Пошук доступних залів")
.WithDescription("Повертає список залів, які вміщують вказану або більшу кількість осіб.");

hallsApi.MapPost("/book", ([FromBody] BookingRequest request, [FromServices] IHallService hallService) =>
{
    try
    {
        var response = hallService.BookHall(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
.WithSummary("Бронювання залу та розрахунок вартості")
.WithDescription("Оформлює бронювання на вказаний час та автоматично підраховує загальну вартість оренди з урахуванням послуг.");

app.Run();

public partial class Program { }