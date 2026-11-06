using HallRental.API.Data;
using HallRental.API.Domain;
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
        HallValidator.Validate(hall);

        hall.Id = Guid.NewGuid();
        hall.Accessories = NormalizeAccessories(hall.Accessories);
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
            throw new DomainValidationException("Hall id must be specified.");

        var hall = GetHall(request.HallId);

        ValidateBookingTime(request.StartTime, request.EndTime);

        // JSON "selectedAccessories": null bypasses the property initializer; treat it as "none".
        var selectedAccessories = ResolveSelectedAccessories(hall, request.SelectedAccessories ?? []);

        bool isAlreadyBooked = _repository.Bookings.Any(b =>
            b.HallId == request.HallId &&
            b.StartTime < request.EndTime &&
            b.EndTime > request.StartTime);

        if (isAlreadyBooked)
        {
            throw new BookingConflictException(request.HallId);
        }

        decimal totalHallCost = CalculateTotalHallCost(hall.BasePricePerHour, request.StartTime, request.EndTime);
        decimal accessoriesCost = selectedAccessories.Sum(a => a.Price);

        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            HallId = request.HallId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            // Canonical hall names, so analytics never sees "wi-fi" and "Wi-Fi" as different services.
            SelectedAccessories = selectedAccessories.Select(a => a.Name).ToList(),
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
    public void Update(Guid id, HallDto updatedHall)
    {
        HallValidator.Validate(updatedHall);

        var hall = GetHall(id);

        hall.Name = updatedHall.Name;
        hall.Capacity = updatedHall.Capacity;
        hall.BasePricePerHour = updatedHall.BasePricePerHour;
        hall.Accessories = NormalizeAccessories(updatedHall.Accessories);

        _repository.SaveChanges();
    }

    /// <summary>
    /// Adds a single accessory to the hall's existing list (without removing the others).
    /// </summary>
    public void AddAccessory(Guid hallId, AccessoryDto accessory)
    {
        HallValidator.ValidateAccessory(accessory);

        var hall = GetHall(hallId);

        var normalized = Normalize(accessory);
        var existing = hall.Accessories.FirstOrDefault(a =>
            AccessoryNames.Comparer.Equals(AccessoryNames.Normalize(a.Name), normalized.Name));
        if (existing != null)
        {
            existing.Price = normalized.Price;
        }
        else
        {
            hall.Accessories.Add(normalized);
        }

        _repository.SaveChanges();
    }

    /// <summary>
    /// Deletes a conference hall by its id.
    /// </summary>
    public void Delete(Guid id)
    {
        var hall = GetHall(id);

        _repository.RemoveHall(hall);
        _repository.SaveChanges();
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

    private static List<AccessoryDto> NormalizeAccessories(IEnumerable<AccessoryDto> accessories) =>
        accessories.Select(Normalize).ToList();

    private static AccessoryDto Normalize(AccessoryDto accessory) =>
        new() { Name = AccessoryNames.Normalize(accessory.Name), Price = accessory.Price };

    private HallDto GetHall(Guid id) =>
        _repository.Halls.FirstOrDefault(h => h.Id == id) ?? throw new HallNotFoundException(id);

    private static void ValidateBookingTime(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new DomainValidationException("Booking end time must be later than start time.");
    }

    /// <summary>
    /// Maps the selected names to the hall's own accessories, in selection order and without repeats.
    /// </summary>
    /// <exception cref="DomainValidationException">A selected name is not offered by the hall.</exception>
    private static List<AccessoryDto> ResolveSelectedAccessories(HallDto hall, List<string> selectedNames)
    {
        // DistinctBy: halls saved before names were validated may still hold duplicates.
        var hallAccessoriesByName = hall.Accessories
            .DistinctBy(a => AccessoryNames.Normalize(a.Name), AccessoryNames.Comparer)
            .ToDictionary(a => AccessoryNames.Normalize(a.Name), AccessoryNames.Comparer);

        var normalizedNames = selectedNames
            .Select(AccessoryNames.Normalize)
            .Distinct(AccessoryNames.Comparer)
            .ToList();

        var unknownNames = normalizedNames
            .Where(name => !hallAccessoriesByName.ContainsKey(name))
            .Select(name => $"'{name}'")
            .ToList();

        if (unknownNames.Count > 0)
            throw new DomainValidationException(
                $"The hall does not offer these accessories: {string.Join(", ", unknownNames)}.");

        return normalizedNames.Select(name => hallAccessoriesByName[name]).ToList();
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
}
