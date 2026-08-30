using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Реєстрація сервісу як Singleton (оскільки ми зберігаємо дані в пам'яті)
builder.Services.AddSingleton<IHallService, HallService>();

var app = builder.Build();

// Групування маршрутів для версії API
var hallsApi = app.MapGroup("/api/v1/halls");

// 1. Додавання конференц-залу (використовуємо лямбда-вираз => замість delegate)
hallsApi.MapPost("/", ([FromBody] HallDto hall, IHallService hallService) =>
{
    var id = hallService.AddHall(hall);
    return Results.Created($"/api/v1/halls/{id}", new
    {
        Message = "Конференц-зал успішно створено",
        Data = new { hall_id = id }
    });
});

// 2. Пошук доступних залів
hallsApi.MapGet("/", ([FromQuery] int minCapacity, IHallService hallService) =>
{
    var halls = hallService.GetAvailableHalls(minCapacity);
    return Results.Ok(halls);
});

app.Run();

// Робимо клас Program публічним, щоб до нього мали доступ проєкти з інтеграційними тестами
public partial class Program { }