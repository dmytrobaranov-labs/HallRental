using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IHallService, HallService>();

var app = builder.Build();

var hallsApi = app.MapGroup("/api/v1/halls");

// 1. Додавання залу
hallsApi.MapPost("/", ([FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var id = hallService.AddHall(hall);
    return Results.Created($"/api/v1/halls/{id}", new
    {
        Message = "Конференц-зал успішно створено",
        Data = new { hall_id = id }
    });
});

// 2. Пошук залів
hallsApi.MapGet("/", ([FromQuery] int minCapacity, [FromServices] IHallService hallService) =>
{
    var halls = hallService.GetAvailableHalls(minCapacity);
    return Results.Ok(halls);
});

// 3. Бронювання та розрахунок вартості
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
});

app.Run();

public partial class Program { }