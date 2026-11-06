using HallRental.API.Domain.Errors;
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
    public void Book_OverlappingInterval_ThrowsBookingConflictException()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 });

        var request1 = new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 14, 0, 0)
        };

        // The first booking succeeds
        _service.Book(request1);

        var request2 = new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 12, 0, 0), // Overlaps the first booking
            EndTime = new DateTime(2026, 9, 1, 15, 0, 0)
        };

        var exception = Assert.Throws<BookingConflictException>(() => _service.Book(request2));
        Assert.Equal(hallId, exception.HallId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Book_EndTimeNotAfterStartTime_ThrowsDomainValidationException(int hours)
    {
        var hallId = _service.Add(new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 });
        var start = new DateTime(2026, 9, 1, 10, 0, 0);

        Assert.Throws<DomainValidationException>(() => _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = start,
            EndTime = start.AddHours(hours)
        }));
    }

    [Fact]
    public void Book_NonExistentHall_ThrowsHallNotFoundException()
    {
        var missingHallId = Guid.NewGuid();

        var exception = Assert.Throws<HallNotFoundException>(() => _service.Book(new BookingRequest
        {
            HallId = missingHallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0)
        }));
        Assert.Equal(missingHallId, exception.HallId);
    }

    [Fact]
    public void GetAvailable_ShouldExcludeBookedHalls_ForGivenTimeRange()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал А", Capacity = 50, BasePricePerHour = 1000 });

        // Book the hall from 10:00 to 12:00
        _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0)
        });

        // Search for free halls in an overlapping interval (11:00 to 13:00)
        var availableHalls = _service.GetAvailable(
            minCapacity: 30,
            startTime: new DateTime(2026, 9, 1, 11, 0, 0),
            endTime: new DateTime(2026, 9, 1, 13, 0, 0)
        ).ToList();

        // The hall must not be listed as available
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
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0) // 2 hours * 1000 = 2000
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
    public void AddAccessory_EmptyName_ThrowsDomainValidationException()
    {
        var hallId = _service.Add(new HallDto { Name = "Зал", Capacity = 10, BasePricePerHour = 500 });

        Assert.Throws<DomainValidationException>(() =>
            _service.AddAccessory(hallId, new AccessoryDto { Name = "", Price = 700 }));
    }

    [Theory]
    [InlineData("", 10, 100)]
    [InlineData("   ", 10, 100)]
    [InlineData("Hall", 0, 100)]
    [InlineData("Hall", -5, 100)]
    [InlineData("Hall", 10, -0.01)]
    public void Add_InvalidHallFields_ThrowsDomainValidationException(string name, int capacity, double basePrice)
    {
        var hall = new HallDto { Name = name, Capacity = capacity, BasePricePerHour = (decimal)basePrice };

        Assert.Throws<DomainValidationException>(() => _service.Add(hall));
    }

    [Theory]
    [InlineData("", 10, 100)]
    [InlineData("   ", 10, 100)]
    [InlineData("Hall", 0, 100)]
    [InlineData("Hall", -5, 100)]
    [InlineData("Hall", 10, -0.01)]
    public void Update_InvalidHallFields_ThrowsDomainValidationException(string name, int capacity, double basePrice)
    {
        var hallId = AddValidHall();
        var hall = new HallDto { Name = name, Capacity = capacity, BasePricePerHour = (decimal)basePrice };

        Assert.Throws<DomainValidationException>(() => _service.Update(hallId, hall));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Sound", -1)]
    public void Add_InvalidAccessory_ThrowsDomainValidationException(string accessoryName, double accessoryPrice)
    {
        var hall = ValidHallWith(new AccessoryDto { Name = accessoryName, Price = (decimal)accessoryPrice });

        Assert.Throws<DomainValidationException>(() => _service.Add(hall));
    }

    [Theory]
    [InlineData("", 10)]
    [InlineData("   ", 10)]
    [InlineData("Sound", -1)]
    public void Update_InvalidAccessory_ThrowsDomainValidationException(string accessoryName, double accessoryPrice)
    {
        var hallId = AddValidHall();
        var hall = ValidHallWith(new AccessoryDto { Name = accessoryName, Price = (decimal)accessoryPrice });

        Assert.Throws<DomainValidationException>(() => _service.Update(hallId, hall));
    }

    [Fact]
    public void Add_DuplicateAccessoryNamesIgnoringCase_ThrowsDomainValidationException()
    {
        var hall = ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 10 }, new AccessoryDto { Name = "wi-fi", Price = 20 });

        Assert.Throws<DomainValidationException>(() => _service.Add(hall));
    }

    [Fact]
    public void Add_ValidAccessories_ReturnsId()
    {
        var hall = ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 0 }, new AccessoryDto { Name = "Sound", Price = 700 });

        Assert.NotEqual(Guid.Empty, _service.Add(hall));
    }

    [Fact]
    public void Update_InvalidHall_KeepsExistingData()
    {
        var hallId = AddValidHall();

        Assert.Throws<DomainValidationException>(() =>
            _service.Update(hallId, new HallDto { Name = "", Capacity = 0, BasePricePerHour = -1 }));

        var hall = _service.GetAvailable(0).First(h => h.Id == hallId);
        Assert.Equal("Valid Hall", hall.Name);
        Assert.Equal(10, hall.Capacity);
        Assert.Equal(100, hall.BasePricePerHour);
    }

    [Fact]
    public void Update_DuplicateAccessoryNamesIgnoringCase_ThrowsWithDuplicateNameInMessage()
    {
        var hallId = AddValidHall();
        var hall = ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 10 }, new AccessoryDto { Name = "WI-FI", Price = 20 });

        var exception = Assert.Throws<DomainValidationException>(() => _service.Update(hallId, hall));

        Assert.Contains("Wi-Fi", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddAccessory_NegativePrice_ThrowsDomainValidationException()
    {
        var hallId = _service.Add(new HallDto { Name = "Hall", Capacity = 10, BasePricePerHour = 500 });

        Assert.Throws<DomainValidationException>(() =>
            _service.AddAccessory(hallId, new AccessoryDto { Name = "Sound", Price = -1 }));
    }

    [Fact]
    public void AddAccessory_SameNameInDifferentCase_UpdatesPriceInsteadOfAddingDuplicate()
    {
        var hallId = _service.Add(ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 100 }));

        _service.AddAccessory(hallId, new AccessoryDto { Name = "wi-fi", Price = 150 });

        var hall = _service.GetAvailable(0).First(h => h.Id == hallId);
        var accessory = Assert.Single(hall.Accessories);
        Assert.Equal("Wi-Fi", accessory.Name);
        Assert.Equal(150, accessory.Price);
    }

    [Fact]
    public void Book_AccessoryNameInDifferentCase_ChargesAccessory()
    {
        var hallId = _service.Add(ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 300 }));

        var response = _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0), // standard hours: 2 x 100
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0),
            SelectedAccessories = ["WI-FI"]
        });

        Assert.Equal(500, response.TotalCost);
    }

    [Fact]
    public void Book_SameAccessorySelectedTwiceInDifferentCase_ChargesItOnce()
    {
        var hallId = _service.Add(ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 300 }));

        var response = _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0),
            SelectedAccessories = ["Wi-Fi", "wi-fi"]
        });

        Assert.Equal(500, response.TotalCost);
    }

    [Fact]
    public void Book_UnknownAccessories_ThrowsWithUnknownNamesInMessage()
    {
        var hallId = _service.Add(ValidHallWith(new AccessoryDto { Name = "Wi-Fi", Price = 300 }));

        var exception = Assert.Throws<DomainValidationException>(() => _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0),
            SelectedAccessories = ["wi-fi", "Catering", "Projector"]
        }));

        Assert.Contains("Catering", exception.Message);
        Assert.Contains("Projector", exception.Message);
        Assert.DoesNotContain("wi-fi", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Book_UnknownAccessory_DoesNotCreateBooking()
    {
        var hallId = AddValidHall();

        Assert.Throws<DomainValidationException>(() => _service.Book(new BookingRequest
        {
            HallId = hallId,
            StartTime = new DateTime(2026, 9, 1, 10, 0, 0),
            EndTime = new DateTime(2026, 9, 1, 12, 0, 0),
            SelectedAccessories = ["Catering"]
        }));

        Assert.Equal(0, _service.GetAnalyticsSummary().TotalBookings);
    }

    private Guid AddValidHall() => _service.Add(ValidHallWith());

    private static HallDto ValidHallWith(params AccessoryDto[] accessories) => new()
    {
        Name = "Valid Hall",
        Capacity = 10,
        BasePricePerHour = 100,
        Accessories = [.. accessories]
    };
}
