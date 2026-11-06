using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace HallRental.IntegrationTests;

public class ProductionErrorHandlingTests : IClassFixture<ProductionWebApplicationFactory>
{
    private const string ProblemJsonMediaType = "application/problem+json";
    private const string HallsRoute = "/api/v1/halls";
    private const string BookRoute = "/api/v1/halls/book";

    private readonly ProductionWebApplicationFactory _factory;

    public ProductionErrorHandlingTests(ProductionWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Book_MalformedJson_Returns400ProblemDetails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(_factory.Services, TestTokens.UserRole));
        using var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(BookRoute, body);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddHall_WithoutToken_Returns401ProblemDetails()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(HallsRoute, new { Name = "Hall", Capacity = 10, BasePricePerHour = 100 });

        await AssertProblemDetailsAsync(response, HttpStatusCode.Unauthorized);
    }

    private static async Task AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
    }
}
