using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HallRental.API.Models;
using HallRental.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace HallRental.IntegrationTests;

public class ErrorHandlingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string ProblemJsonMediaType = "application/problem+json";
    private const string HallsRoute = "/api/v1/halls";
    private const string BookRoute = "/api/v1/halls/book";

    // Fixed dates far from "now": booking rules do not depend on the current time.
    private static readonly DateTime BookingStart = new(2030, 1, 10, 10, 0, 0, DateTimeKind.Utc);

    private readonly WebApplicationFactory<Program> _factory;

    public ErrorHandlingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Book_OverlappingInterval_Returns409ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);
        var hallId = await CreateHallAsync(client);
        var firstBooking = await client.PostAsJsonAsync(BookRoute, BookingFor(hallId, BookingStart, hours: 2));
        Assert.Equal(HttpStatusCode.OK, firstBooking.StatusCode);

        var response = await client.PostAsJsonAsync(BookRoute, BookingFor(hallId, BookingStart.AddHours(1), hours: 2));

        await AssertProblemDetailsAsync(response, HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Book_EndTimeNotAfterStartTime_Returns400ProblemDetails(int hours)
    {
        var client = CreateClient(TestTokens.UserRole);
        var hallId = await CreateHallAsync(CreateClient(TestTokens.AdminRole));

        var response = await client.PostAsJsonAsync(BookRoute, BookingFor(hallId, BookingStart, hours));

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Book_NonExistentHall_Returns404ProblemDetails()
    {
        var client = CreateClient(TestTokens.UserRole);

        var response = await client.PostAsJsonAsync(BookRoute, BookingFor(Guid.NewGuid(), BookingStart, hours: 2));

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Book_MalformedJson_Returns400ProblemDetails()
    {
        var client = CreateClient(TestTokens.UserRole);
        using var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await client.PostAsync(BookRoute, body);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddHall_InvalidCapacity_Returns400ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);

        var response = await client.PostAsJsonAsync(HallsRoute, new HallDto { Name = "Hall", Capacity = 0, BasePricePerHour = 100 });

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("", 10, 100)]
    [InlineData("Hall", 0, 100)]
    [InlineData("Hall", 10, -1)]
    public async Task UpdateHall_InvalidFields_Returns400ProblemDetails(string name, int capacity, int basePrice)
    {
        var client = CreateClient(TestTokens.AdminRole);
        var hallId = await CreateHallAsync(client);

        var response = await client.PutAsJsonAsync($"{HallsRoute}/{hallId}", new HallDto { Name = name, Capacity = capacity, BasePricePerHour = basePrice });

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateHall_DuplicateAccessoryNamesIgnoringCase_Returns400ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);
        var hallId = await CreateHallAsync(client);
        var hall = new HallDto
        {
            Name = "Hall",
            Capacity = 10,
            BasePricePerHour = 100,
            Accessories = [new AccessoryDto { Name = "Wi-Fi", Price = 10 }, new AccessoryDto { Name = "wi-fi", Price = 20 }]
        };

        var response = await client.PutAsJsonAsync($"{HallsRoute}/{hallId}", hall);

        await AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateHall_NonExistentHall_Returns404ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);

        var response = await client.PutAsJsonAsync($"{HallsRoute}/{Guid.NewGuid()}", new HallDto { Name = "Hall", Capacity = 10, BasePricePerHour = 100 });

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddAccessory_NonExistentHall_Returns404ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);

        var response = await client.PostAsJsonAsync($"{HallsRoute}/{Guid.NewGuid()}/accessories", new AccessoryDto { Name = "Sound", Price = 100 });

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteHall_NonExistentHall_Returns404ProblemDetails()
    {
        var client = CreateClient(TestTokens.AdminRole);

        var response = await client.DeleteAsync($"{HallsRoute}/{Guid.NewGuid()}");

        await AssertProblemDetailsAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnyEndpoint_UnexpectedException_Returns500WithoutInternalDetails()
    {
        var client = _factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddScoped<IHallService, FailingHallService>()))
            .CreateClient();

        var response = await client.GetAsync($"{HallsRoute}?minCapacity=1");

        var problem = await AssertProblemDetailsAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain(FailingHallService.SensitiveMessage, await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    private HttpClient CreateClient(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestTokens.Create(_factory.Services, role));
        return client;
    }

    private static async Task<Guid> CreateHallAsync(HttpClient adminClient)
    {
        var response = await adminClient.PostAsJsonAsync(HallsRoute, new HallDto { Name = "Error Handling Hall", Capacity = 20, BasePricePerHour = 1000 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static BookingRequest BookingFor(Guid hallId, DateTime start, int hours) => new()
    {
        HallId = hallId,
        StartTime = start,
        EndTime = start.AddHours(hours)
    };

    private static async Task<ProblemDetails> AssertProblemDetailsAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expectedStatus, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        return problem;
    }

    /// <summary>Simulates an infrastructure failure whose message must never reach the client.</summary>
    private sealed class FailingHallService : IHallService
    {
        public const string SensitiveMessage = "Login failed for user 'sa' on server sql-internal-01";

        public Guid Add(HallDto hall) => throw Failure();
        public IEnumerable<HallDto> GetAvailable(int minCapacity, DateTime? startTime = null, DateTime? endTime = null) => throw Failure();
        public BookingResponse Book(BookingRequest request) => throw Failure();
        public bool Update(Guid id, HallDto updatedHall) => throw Failure();
        public bool AddAccessory(Guid hallId, AccessoryDto accessory) => throw Failure();
        public bool Delete(Guid id) => throw Failure();
        public AnalyticsSummaryDto GetAnalyticsSummary() => throw Failure();

        private static InvalidOperationException Failure() => new(SensitiveMessage);
    }
}
