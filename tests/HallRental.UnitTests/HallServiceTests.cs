using HallRental.API.Models;
using HallRental.API.Services;
using Xunit;

namespace HallRental.UnitTests;

public class HallServiceTests
{
    private readonly IHallService _service;

    public HallServiceTests()
    {
        _service = new HallService();
    }

    [Fact]
    public void Add_ShouldReturnValidGuid()
    {
        var hall = new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 };
        var id = _service.Add(hall);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public void GetAvailable_ShouldReturnFilteredHalls_ByCapacity()
    {
        _service.Add(new HallDto { Name = "Малий зал", Capacity = 20, BasePricePerHour = 500 });
        _service.Add(new HallDto { Name = "Великий зал", Capacity = 100, BasePricePerHour = 1500 });

        var available = _service.GetAvailable(50).ToList();

        Assert.Single(available);
        Assert.Equal("Великий зал", available[0].Name);
    }

    [Fact]
    public void Book_ShouldThrowException_WhenHallIsAlreadyBooked()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 });

        var request1 = new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 14, 0, 0)
        };

        // Перше бронювання успішне
        _service.Book(request1);

        var request2 = new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 12, 0, 0), // Перетин з першим бронюванням
            EndTime = new DateTime(2026, 9, 1, 15, 0, 0)
        };

        // Друге бронювання має викликати виняток через колізію часу
        var exception = Assert.Throws<Exception>(() => _service.Book(request2));
        Assert.Equal("Цей зал вже заброньований на обраний проміжок часу", exception.Message);
    }

    [Fact]
    public void GetAvailable_ShouldExcludeBookedHalls_ForGivenTimeRange()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 });

        // Бронюємо зал з 10:00 до 12:00
        _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0)
        });

        // Шукаємо вільні зали на інтервал, що перетинається (з 11:00 до 13:00)
        var availableHalls = _service.GetAvailable(
            minCapacity: 30,
            startTime: new DateTime(2026, 9, 1, 11, 0, 0),
            endTime: new DateTime(2026, 9, 1, 13, 0, 0)
        ).ToList();

        // Зал не повинен з'явитися у списку вільних
        Assert.Empty(availableHalls);
    }

    [Fact]
    public void GetAnalyticsSummary_ShouldReturnCorrectMetrics_WhenBookingsExist()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал Зірковий", Capacity = 100, BasePricePerHour = 1000 });

        _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0) // 2 години * 1000 = 2000
        });

        var summary = _service.GetAnalyticsSummary();

        Assert.Equal(1, summary.TotalBookings);
        Assert.Equal(2000, summary.TotalRevenue);
        Assert.Equal("Зал Зірковий", summary.MostPopularHall);
    }

    [Fact]
    public void Update_ShouldModifyHallData_WhenHallExists()
    {
        var hallId = _service.Add(new HallDto { Name = "Старий зал", Capacity = 20, BasePricePerHour = 800 });

        var updated = _service.Update(hallId, new HallDto
        {
            Name = "Новий зал",
            Capacity = 60,
            BasePricePerHour = 2500
        });

        var hall = _service.GetAvailable(0).First(h => h.Id == hallId);

        Assert.True(updated);
        Assert.Equal("Новий зал", hall.Name);
        Assert.Equal(60, hall.Capacity);
        Assert.Equal(2500, hall.BasePricePerHour);
    }

    [Fact]
    public void Update_ShouldReturnFalse_WhenHallDoesNotExist()
    {
        var updated = _service.Update(Guid.NewGuid(), new HallDto { Name = "X", Capacity = 1, BasePricePerHour = 1 });

        Assert.False(updated);
    }

    [Fact]
    public void AddAccessory_ShouldAddNewAccessory_WithoutRemovingExisting()
    {
        var hallId = _service.Add(new HallDto
        {
            Name = "Зал з обладнанням",
            Capacity = 40,
            BasePricePerHour = 1200,
            Accessories = new List<AccessoryDto> { new AccessoryDto { Name = "Проєктор", Price = 500 } }
        });

        var added = _service.AddAccessory(hallId, new AccessoryDto { Name = "Звук", Price = 700 });
        var hall = _service.GetAvailable(0).First(h => h.Id == hallId);

        Assert.True(added);
        Assert.Equal(2, hall.Accessories.Count);
        Assert.Contains(hall.Accessories, a => a.Name == "Проєктор" && a.Price == 500);
        Assert.Contains(hall.Accessories, a => a.Name == "Звук" && a.Price == 700);
    }

    [Fact]
    public void AddAccessory_ShouldUpdatePrice_WhenAccessoryNameAlreadyExists()
    {
        var hallId = _service.Add(new HallDto
        {
            Name = "Зал з обладнанням",
            Capacity = 40,
            BasePricePerHour = 1200,
            Accessories = new List<AccessoryDto> { new AccessoryDto { Name = "Звук", Price = 500 } }
        });

        var added = _service.AddAccessory(hallId, new AccessoryDto { Name = "Звук", Price = 700 });
        var hall = _service.GetAvailable(0).First(h => h.Id == hallId);

        Assert.True(added);
        Assert.Single(hall.Accessories);
        Assert.Equal(700, hall.Accessories[0].Price);
    }

    [Fact]
    public void AddAccessory_ShouldReturnFalse_WhenHallDoesNotExist()
    {
        var added = _service.AddAccessory(Guid.NewGuid(), new AccessoryDto { Name = "Звук", Price = 700 });

        Assert.False(added);
    }

    [Fact]
    public void AddAccessory_ShouldThrow_WhenNameIsEmpty()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал", Capacity = 10, BasePricePerHour = 500 });

        Assert.Throws<ArgumentException>(() =>
            _service.AddAccessory(hallId, new AccessoryDto { Name = "", Price = 700 }));
    }
}