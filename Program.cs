using System.Reflection;
using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
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

hallsApi.MapPost("/", ([FromBody] HallDto hall, [FromServices] IHallService hallService) =>
{
    var id = hallService.Add(hall);
    return Results.Created($"/api/v1/halls/{id}", new { Message = "Конференц-зал успішно створено", Data = new { hall_id = id } });
})
.WithSummary("Створення нового конференц-залу")
.WithDescription("Додає новий зал із заданою місткістю, базовою ціною та переліком послуг до системи.");

hallsApi.MapGet("/", ([FromQuery] int minCapacity, [FromServices] IHallService hallService) =>
{
    var halls = hallService.GetAvailable(minCapacity);
    return Results.Ok(halls);
})
.WithSummary("Пошук доступних залів")
.WithDescription("Повертає список залів, які вміщують вказану або більшу кількість осіб.");

hallsApi.MapPost("/book", ([FromBody] BookingRequest request, [FromServices] IHallService hallService) =>
{
    try
    {
        var response = hallService.Book(request);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { Error = ex.Message });
    }
})
.WithSummary("Бронювання залу та розрахунок вартості")
.WithDescription("Оформлює бронювання на вказаний час та автоматично підраховує загальну вартість оренди з урахуванням послуг.");

hallsApi.MapPut("/{id:guid}", ([FromRoute] Guid id, [FromBody] HallDto updatedHall, [FromServices] IHallService hallService) =>
{
    var success = hallService.Update(id, updatedHall);
    return success
        ? Results.Ok(new { Message = "Зал успішно оновлено" })
        : Results.NotFound(new { Error = "Зал не знайдено" });
})
.WithSummary("Редагування інформації про зал")
.WithDescription("Оновлює дані існуючого залу (наприклад, зміна вартості оренди або додавання послуг).");

hallsApi.MapDelete("/{id:guid}", ([FromRoute] Guid id, [FromServices] IHallService hallService) =>
{
    var success = hallService.Delete(id);
    return success
        ? Results.Ok(new { Message = "Зал успішно видалено" })
        : Results.NotFound(new { Error = "Зал не знайдено" });
})
.WithSummary("Видалення конференц-залу")
.WithDescription("Видаляє зал із системи за його унікальним ідентифікатором.");

app.Run();

public partial class Program { }