using HallRental.API.Data;
using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Сервіс для управління конференц-залами та обробки бронювань
/// з урахуванням часових колізій та динамічних тарифів.
/// Дані зберігаються через <see cref="IHallRepository"/> (пам'ять або Azure SQL).
/// </summary>
public class HallService : IHallService
{
    private readonly IHallRepository _repository;

    // Допоміжний калькулятор для обчислення динамічної ціни залежно від часу доби
    private readonly PricingCalculator _pricingCalculator = new();

    /// <summary>
    /// Конструктор без параметрів — зберігає сумісність із наявними модульними тестами
    /// (<c>new HallService()</c>): використовує сховище в пам'яті.
    /// </summary>
    public HallService() : this(new InMemoryHallRepository()) { }

    public HallService(IHallRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Додає новий конференц-зал до системи.
    /// </summary>
    public Guid Add(HallDto hall)
    {
        if (string.IsNullOrWhiteSpace(hall.Name))
            throw new ArgumentException("Назва залу не може бути порожньою");

        if (hall.Capacity <= 0)
            throw new ArgumentException("Місткість залу має бути більшою за нуль");

        if (hall.BasePricePerHour < 0)
            throw new ArgumentException("Базова ціна не може бути від'ємною");

        hall.Id = Guid.NewGuid();
        _repository.AddHall(hall);
        _repository.SaveChanges();
        return hall.Id;
    }

    /// <summary>
    /// Пошук доступних залів за мінімальною місткістю та відсіканням вже зайнятих на вказаний період.
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
    /// Оформлює бронювання залу з перевіркою колізій та розрахунком вартості за динамічним тарифом.
    /// </summary>
    public BookingResponse Book(BookingRequest request)
    {
        if (request.HallId == Guid.Empty)
            throw new ArgumentException("Не вказано ідентифікатор залу");

        var hall = _repository.Halls.FirstOrDefault(h => h.Id == request.HallId);
        if (hall == null)
            throw new KeyNotFoundException("Зал не знайдено");

        ValidateBookingTime(request.StartTime, request.EndTime);

        bool isAlreadyBooked = _repository.Bookings.Any(b =>
            b.HallId == request.HallId &&
            b.StartTime < request.EndTime &&
            b.EndTime > request.StartTime);

        if (isAlreadyBooked)
        {
            throw new Exception("Цей зал вже заброньований на обраний проміжок часу");
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
            Message = "Бронювання успішне з урахуванням динамічного тарифу"
        };
    }

    /// <summary>
    /// Оновлює інформацію про існуючий зал.
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
    /// Додає одну додаткову послугу до наявного списку послуг залу (без видалення інших).
    /// </summary>
    public bool AddAccessory(Guid hallId, AccessoryDto accessory)
    {
        var hall = _repository.Halls.FirstOrDefault(h => h.Id == hallId);
        if (hall == null)
            return false;

        if (string.IsNullOrWhiteSpace(accessory.Name))
            throw new ArgumentException("Назва послуги не може бути порожньою");

        if (accessory.Price < 0)
            throw new ArgumentException("Вартість послуги не може бути від'ємною");

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
    /// Видаляє конференц-зал із системи за його ідентифікатором.
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
    /// Генерує зведену аналітичну звітність.
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
            MostPopularHall = popularHall?.Name ?? "Невідомо"
        };
    }

    private static void ValidateBookingTime(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new Exception("Некоректний час бронювання");
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
