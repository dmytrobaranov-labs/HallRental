using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HallRental.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HallRental.IntegrationTests;

public class BookingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string HallsRoute = "/api/v1/halls";
    private const string BookRoute = "/api/v1/halls/book";

    // Fixed date far from "now"; 10:00-12:00 UTC falls in standard (non-peak) hours.
    private static readonly DateTime BookingStart = new(2030, 2, 5, 10, 0, 0, DateTimeKind.Utc);

    private readonly WebApplicationFactory<Program> _factory;

    public BookingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Book_AccessoryNameInDifferentCase_Returns200AndChargesAccessory()
    {
        var hallId = await CreateHallAsync(CreateClient(TestTokens.AdminRole), basePricePerHour: 1000,
            new AccessoryDto { Name = "Wi-Fi", Price = 300 });
        var client = CreateClient(TestTokens.UserRole);

        var response = await client.PostAsJsonAsync(BookRoute, new BookingRequest
        {
            HallId = hallId,
            StartTime = BookingStart,
            EndTime = BookingStart.AddHours(2),
            SelectedAccessories = ["wi-fi"]
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);
        Assert.Equal(2300m, booking.TotalCost); // 2 h x 1000 + 300
    }

    [Fact]
    public async Task Book_NullSelectedAccessories_Returns200AndChargesHallOnly()
    {
        var hallId = await CreateHallAsync(CreateClient(TestTokens.AdminRole), basePricePerHour: 1000,
            new AccessoryDto { Name = "Wi-Fi", Price = 300 });
        var client = CreateClient(TestTokens.UserRole);

        // Explicit JSON null: System.Text.Json overwrites the property initializer with null.
        var response = await client.PostAsJsonAsync(BookRoute, new
        {
            hallId,
            startTime = BookingStart.AddDays(1),
            endTime = BookingStart.AddDays(1).AddHours(2),
            selectedAccessories = (string[]?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var booking = await response.Content.ReadFromJsonAsync<BookingResponse>();
        Assert.NotNull(booking);
        Assert.Equal(2000m, booking.TotalCost); // 2 h x 1000, no accessories
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(_factory.Services, role));
        return client;
    }

    private static async Task<Guid> CreateHallAsync(HttpClient adminClient, decimal basePricePerHour, params AccessoryDto[] accessories)
    {
        var hall = new HallDto { Name = "Booking Hall", Capacity = 20, BasePricePerHour = basePricePerHour, Accessories = [.. accessories] };
        var response = await adminClient.PostAsJsonAsync(HallsRoute, hall);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return created.GetProperty("id").GetGuid();
    }
}
