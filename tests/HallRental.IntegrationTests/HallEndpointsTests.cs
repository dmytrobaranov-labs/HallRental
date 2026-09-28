using HallRental.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace HallRental.IntegrationTests;

public class HallEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    // Тестові токени, підписані тим самим секретом, що й у Program.cs
    // ("SuperSecretKey12345678901234567890"), з коректними claim'ами role/iss/aud.
    private const string AdminToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJhZG1pbi1pZC0wMDEiLCJuYW1lIjoiQWRtaW4gVXNlciIsInJvbGUiOiJBZG1pbiIsIm5iZiI6MTc1Njg4MjU3OCwiZXhwIjoyMDcyNDgyNTc4LCJpc3MiOiJIYWxsUmVudGFsQVBJIiwiYXVkIjoiSGFsbFJlbnRhbENsaWVudCJ9.ag7TeByv5SDWw-9xG_gVdbkukl2zklF7hTZaWLHnytQ";

    private const string UserToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ1c2VyLWlkLTAwMiIsIm5hbWUiOiJSZWd1bGFyIENsaWVudCIsInJvbGUiOiJVc2VyIiwibmJmIjoxNzU2ODgyNTc4LCJleHAiOjIwNzI0ODI1NzgsImlzcyI6IkhhbGxSZW50YWxBUEkiLCJhdWQiOiJIYWxsUmVudGFsQ2xpZW50In0.VbDhiYpOEICHHp4U2oBb94BiXgN5DjWC0_Hz7vrEoaY";

    public HallEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private void UseToken(string? token)
    {
        _client.DefaultRequestHeaders.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<Guid> CreateHallAsync(string name = "Test Hall", int capacity = 50, decimal basePrice = 2000)
    {
        UseToken(AdminToken);
        var newHall = new HallDto { Name = name, Capacity = capacity, BasePricePerHour = basePrice };
        var response = await _client.PostAsJsonAsync("/api/v1/halls", newHall);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // POST /halls повертає { "id": "<guid>" }, а НЕ { "data": { "hall_id": ... } }
        var result = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return result.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Post_Add_ReturnsCreatedAndId()
    {
        UseToken(AdminToken); // POST /halls вимагає AdminOnly

        var newHall = new HallDto { Name = "Зал A", Capacity = 50, BasePricePerHour = 2000 };
        var response = await _client.PostAsJsonAsync("/api/v1/halls", newHall);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_Add_WithoutToken_ReturnsUnauthorized()
    {
        UseToken(null);

        var newHall = new HallDto { Name = "Зал без токена", Capacity = 10, BasePricePerHour = 500 };
        var response = await _client.PostAsJsonAsync("/api/v1/halls", newHall);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Add_WithUserToken_ReturnsForbidden()
    {
        UseToken(UserToken); // валідний токен, але без ролі Admin

        var newHall = new HallDto { Name = "Зал не для юзера", Capacity = 10, BasePricePerHour = 500 };
        var response = await _client.PostAsJsonAsync("/api/v1/halls", newHall);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_Halls_ReturnsOkStatus()
    {
        // Публічний ендпоінт, токен не потрібен
        UseToken(null);

        var response = await _client.GetAsync("/api/v1/halls?minCapacity=30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_Book_WithNonExistentHall_ReturnsNotFound()
    {
        UseToken(UserToken); // /book вимагає лише автентифікації (будь-яка роль)

        var request = new BookingRequest
        {
            HallId = Guid.NewGuid(), // такого залу не існує
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2)
        };

        var response = await _client.PostAsJsonAsync("/api/v1/halls/book", request);

        // HallService.Book кидає KeyNotFoundException -> middleware мапить її в 404, а не 400
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Book_WithoutToken_ReturnsUnauthorized()
    {
        UseToken(null);

        var request = new BookingRequest
        {
            HallId = Guid.NewGuid(),
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow.AddHours(2)
        };

        var response = await _client.PostAsJsonAsync("/api/v1/halls/book", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_Update_ReturnsOk_WhenSuccessful()
    {
        var hallId = await CreateHallAsync();

        UseToken(AdminToken); // PUT /halls/{id} вимагає AdminOnly
        var updatedHall = new HallDto { Name = "Updated Hall", Capacity = 100, BasePricePerHour = 3000 };
        var putResponse = await _client.PutAsJsonAsync($"/api/v1/halls/{hallId}", updatedHall);

        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);
    }

    [Fact]
    public async Task Put_Update_WithNonExistentHall_ReturnsNotFound()
    {
        UseToken(AdminToken);

        var updatedHall = new HallDto { Name = "Немає такого залу", Capacity = 10, BasePricePerHour = 500 };
        var response = await _client.PutAsJsonAsync($"/api/v1/halls/{Guid.NewGuid()}", updatedHall);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddAccessory_ReturnsOk_WhenHallExists()
    {
        var hallId = await CreateHallAsync();

        UseToken(AdminToken); // POST /halls/{id}/accessories вимагає AdminOnly
        var accessory = new AccessoryDto { Name = "Звук", Price = 700 };
        var response = await _client.PostAsJsonAsync($"/api/v1/halls/{hallId}/accessories", accessory);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_AddAccessory_WithNonExistentHall_ReturnsNotFound()
    {
        UseToken(AdminToken);

        var accessory = new AccessoryDto { Name = "Звук", Price = 700 };
        var response = await _client.PostAsJsonAsync($"/api/v1/halls/{Guid.NewGuid()}/accessories", accessory);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Remove_ReturnsNoContent_WhenSuccessful()
    {
        var hallId = await CreateHallAsync("Test Hall Delete");

        UseToken(AdminToken); // DELETE /halls/{id} вимагає AdminOnly
        var deleteResponse = await _client.DeleteAsync($"/api/v1/halls/{hallId}");

        // Ендпоінт повертає Results.NoContent() (204), а не 200 OK
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_Remove_WithoutToken_ReturnsUnauthorized()
    {
        UseToken(null);

        var response = await _client.DeleteAsync($"/api/v1/halls/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
