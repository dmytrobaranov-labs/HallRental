using HallRental.API.Data;
using HallRental.API.Domain.Errors;
using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Manages conference halls and handles bookings,
/// taking time conflicts and dynamic pricing into account.
/// Data is stored through <see cref="IHallRepository"/> (in memory or Azure SQL).
/// </summary>
public class HallService : IHallService
{
    private readonly IHallRepository _repository;

    // Computes the dynamic hourly rate depending on the time of day
    private readonly PricingCalculator _pricingCalculator = new();

    /// <summary>
    /// Parameterless constructor kept for compatibility with the existing unit tests
    /// (<c>new HallService()</c>): uses the in-memory repository.
    /// </summary>
    public HallService() : this(new InMemoryHallRepository()) { }

    public HallService(IHallRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Adds a new conference hall.
    /// </summary>
    public Guid Add(HallDto hall)
    {
        if (string.IsNullOrWhiteSpace(hall.Name))
            throw new ValidationException("Hall name must not be empty.");

        if (hall.Capacity <= 0)
            throw new ValidationException("Hall capacity must be greater than zero.");

        if (hall.BasePricePerHour < 0)
            throw new ValidationException("Base price must not be negative.");

        hall.Id = Guid.NewGuid();
        _repository.AddHall(hall);
        _repository.SaveChanges();
        return hall.Id;
    }

    /// <summary>
    /// Finds halls with at least the given capacity that are not booked for the given period.
    /// </summary>
    public IEnumerable<HallDto> GetAvailable(int minCapacity, DateTime? startTime = null, DateTime? endTime = null)
    {
        var candidateHalls = _repository.Halls.Where(h => h.Capacity >= minCapacity);

        if (!startTime.HasValue || !endTime.HasValue)
        {
            return candidateHalls.ToList();
        }

        var start = startTime.Value;
        var end = endTime.Value;

        var bookedHallIds = _repository.Bookings
            .Where(b => b.StartTime < end && b.EndTime > start)
            .Select(b => b.HallId)
            .Distinct()
            .ToList();

        return candidateHalls.Where(h => !bookedHallIds.Contains(h.Id)).ToList();
    }

    /// <summary>
    /// Books a hall after checking for conflicts and calculates the cost using dynamic pricing.
    /// </summary>
    public BookingResponse Book(BookingRequest request)
    {
        if (request.HallId == Guid.Empty)
            throw new ValidationException("Hall id must be specified.");

        var hall = _repository.Halls.FirstOrDefault(h => h.Id == request.HallId);
        if (hall == null)
            throw new HallNotFoundException(request.HallId);

        ValidateBookingTime(request.StartTime, request.EndTime);

        bool isAlreadyBooked = _repository.Bookings.Any(b =>
            b.HallId == request.HallId &&
            b.StartTime < request.EndTime &&
            b.EndTime > request.StartTime);

        if (isAlreadyBooked)
        {
            throw new BookingConflictException(request.HallId);
        }

        decimal totalHallCost = CalculateTotalHallCost(hall.BasePricePerHour, request.StartTime, request.EndTime);
        decimal accessoriesCost = CalculateAccessoriesCost(hall.Accessories, request.SelectedAccessories);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            HallId = request.HallId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SelectedAccessories = request.SelectedAccessories.ToList(),
            TotalCost = Math.Round(totalHallCost + accessoriesCost, 2)
        };

        _repository.AddBooking(booking);
        _repository.SaveChanges();

        return new BookingResponse
        {
            BookingId = booking.Id,
            TotalCost = booking.TotalCost,
            Message = "Booking confirmed; the cost includes dynamic pricing."
        };
    }

    /// <summary>
    /// Updates an existing hall.
    /// </summary>
    public bool Update(Guid id, HallDto updatedHall)
    {
        var hall = _repository.Halls.FirstOrDefault(h => h.Id == id);
        if (hall == null)
            return false;

        hall.Name = updatedHall.Name;
        hall.Capacity = updatedHall.Capacity;
        hall.BasePricePerHour = updatedHall.BasePricePerHour;
        hall.Accessories = updatedHall.Accessories;

        _repository.SaveChanges();
        return true;
    }

    /// <summary>
    /// Adds a single accessory to the hall's existing list (without removing the others).
    /// </summary>
    public bool AddAccessory(Guid hallId, AccessoryDto accessory)
    {
        var hall = _repository.Halls.FirstOrDefault(h => h.Id == hallId);
        if (hall == null)
            return false;

        if (string.IsNullOrWhiteSpace(accessory.Name))
            throw new ValidationException("Accessory name must not be empty.");

        if (accessory.Price < 0)
            throw new ValidationException("Accessory price must not be negative.");

        var existing = hall.Accessories.FirstOrDefault(a => a.Name == accessory.Name);
        if (existing != null)
        {
            existing.Price = accessory.Price;
        }
        else
        {
            hall.Accessories.Add(accessory);
        }

        _repository.SaveChanges();
        return true;
    }

    /// <summary>
    /// Deletes a conference hall by its id.
    /// </summary>
    public bool Delete(Guid id)
    {
        var hall = _repository.Halls.FirstOrDefault(h => h.Id == id);
        if (hall == null)
            return false;

        _repository.RemoveHall(hall);
        _repository.SaveChanges();
        return true;
    }

    /// <summary>
    /// Builds the summary analytics report.
    /// </summary>
    public AnalyticsSummaryDto GetAnalyticsSummary()
    {
        if (!_repository.Bookings.Any())
        {
            return new AnalyticsSummaryDto();
        }

        var totalBookings = _repository.Bookings.Count();
        var totalRevenue = _repository.Bookings.Sum(b => b.TotalCost);

        var popularHallId = _repository.Bookings
            .GroupBy(b => b.HallId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .First();

        var popularHall = _repository.Halls.FirstOrDefault(h => h.Id == popularHallId);

        return new AnalyticsSummaryDto
        {
            TotalBookings = totalBookings,
            TotalRevenue = totalRevenue,
            MostPopularHall = popularHall?.Name ?? "Unknown"
        };
    }

    private static void ValidateBookingTime(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new ValidationException("Booking end time must be later than start time.");
    }

    private decimal CalculateTotalHallCost(decimal basePrice, DateTime start, DateTime end)
    {
        decimal totalCost = 0;
        var currentTime = start;

        while (currentTime < end)
        {
            var nextTime = currentTime.AddHours(1);
            double fraction = nextTime > end ? (end - currentTime).TotalHours : 1.0;
            decimal hourlyRate = _pricingCalculator.CalculateHourlyRate(basePrice, currentTime.Hour);
            totalCost += hourlyRate * (decimal)fraction;
            currentTime = nextTime;
        }

        return totalCost;
    }

    private static decimal CalculateAccessoriesCost(List<AccessoryDto> hallAccessories, List<string> selectedAccessories)
    {
        return hallAccessories
            .Where(a => selectedAccessories.Contains(a.Name))
            .Sum(a => a.Price);
    }
}
