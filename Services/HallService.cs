using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Сервіс для управління конференц-залами та обробки бронювань 
/// з урахуванням часових колізій та динамічних тарифів.
/// </summary>
public class HallService : IHallService
{
    // Внутрішнє сховище залів у пам'яті
    private readonly List<HallDto> _halls = new();

    // Колекція для збереження успішних бронювань (використовується для контролю зайнятості)
    private readonly List<(BookingRequest Request, BookingResponse Response)> _bookings = new();

    // Допоміжний калькулятор для обчислення динамічної ціни залежно від часу доби
    private readonly PricingCalculator _pricingCalculator = new();

    /// <summary>
    /// Додає новий конференц-зал до системи.
    /// </summary>
    public Guid Add(HallDto hall)
    {
        hall.Id = Guid.NewGuid();
        _halls.Add(hall);
        return hall.Id;
    }

    /// <summary>
    /// Пошук доступних залів за мінімальною місткістю та відсіканням вже зайнятих на вказаний період.
    /// </summary>
    public IEnumerable<HallDto> GetAvailable(int minCapacity, DateTime? startTime = null, DateTime? endTime = null)
    {
        // Крок 1. Фільтруємо зали за місткістю
        var candidateHalls = _halls.Where(h => h.Capacity >= minCapacity);

        // Якщо часовий інтервал не вказано, повертаємо зали виключно за місткістю
        if (!startTime.HasValue || !endTime.HasValue)
        {
            return candidateHalls;
        }

        // Крок 2. Знаходимо ідентифікатори залів, які мають перетин інтервалів бронювання
        var bookedHallIds = _bookings
            .Where(b => b.Request.StartTime < endTime.Value && b.Request.EndTime > startTime.Value)
            .Select(b => b.Request.HallId)
            .ToHashSet();

        // Крок 3. Виключаємо зайняті зали зі списку доступних
        return candidateHalls.Where(h => !bookedHallIds.Contains(h.Id));
    }

    /// <summary>
    /// Оформлює бронювання залу з перевіркою колізій та розрахунком вартості за динамічним тарифом.
    /// </summary>
    public BookingResponse Book(BookingRequest request)
    {
        // Перевіряємо, чи існує зал
        var hall = _halls.FirstOrDefault(h => h.Id == request.HallId);
        if (hall == null)
            throw new Exception("Зал не знайдено");

        // Перевіряємо коректність введеного часу
        ValidateBookingTime(request.StartTime, request.EndTime);

        // Перевіряємо на конфлікт (подвійне бронювання в той самий час)
        bool isAlreadyBooked = _bookings.Any(b =>
            b.Request.HallId == request.HallId &&
            b.Request.StartTime < request.EndTime &&
            b.Request.EndTime > request.StartTime
        );

        if (isAlreadyBooked)
        {
            throw new Exception("Цей зал вже заброньований на обраний проміжок часу");
        }

        // Розраховуємо вартість оренди залу (з урахуванням ранкових, вечірніх та пікових годин)
        decimal totalHallCost = CalculateTotalHallCost(hall.BasePricePerHour, request.StartTime, request.EndTime);

        // Розраховуємо вартість додаткових послуг (обладнання тощо)
        decimal accessoriesCost = CalculateAccessoriesCost(hall.Accessories, request.SelectedAccessories);

        var response = new BookingResponse
        {
            BookingId = Guid.NewGuid(),
            TotalCost = Math.Round(totalHallCost + accessoriesCost, 2),
            Message = "Бронювання успішне з урахуванням динамічного тарифу"
        };

        // Зберігаємо бронювання в історії для запобігання майбутнім колізіям
        _bookings.Add((request, response));

        return response;
    }

    /// <summary>
    /// Оновлює інформацію про існуючий зал.
    /// </summary>
    public bool Update(Guid id, HallDto updatedHall)
    {
        var hall = _halls.FirstOrDefault(h => h.Id == id);
        if (hall == null)
            return false;

        hall.Name = updatedHall.Name;
        hall.Capacity = updatedHall.Capacity;
        hall.BasePricePerHour = updatedHall.BasePricePerHour;
        hall.Accessories = updatedHall.Accessories;

        return true;
    }

    /// <summary>
    /// Видаляє конференц-зал із системи за його ідентифікатором.
    /// </summary>
    public bool Delete(Guid id)
    {
        var hall = _halls.FirstOrDefault(h => h.Id == id);
        if (hall == null)
            return false;

        _halls.Remove(hall);
        return true;
    }

    /// <summary>
    /// Перевіряє, чи є дата завершення бронювання пізнішою за дату початку.
    /// </summary>
    private void ValidateBookingTime(DateTime start, DateTime end)
    {
        if (end <= start)
            throw new Exception("Некоректний час бронювання");
    }

    /// <summary>
    /// Погодинно обчислює вартість оренди залу із залученням PricingCalculator.
    /// </summary>
    private decimal CalculateTotalHallCost(decimal basePrice, DateTime start, DateTime end)
    {
        decimal totalCost = 0;
        var currentTime = start;

        while (currentTime < end)
        {
            var nextTime = currentTime.AddHours(1);

            // Якщо остання година неповна, беремо пропорційну частку
            double fraction = nextTime > end ? (end - currentTime).TotalHours : 1.0;

            // Отримуємо ставку за конкретну годину доби
            decimal hourlyRate = _pricingCalculator.CalculateHourlyRate(basePrice, currentTime.Hour);

            totalCost += hourlyRate * (decimal)fraction;
            currentTime = nextTime;
        }

        return totalCost;
    }

    /// <summary>
    /// Підраховує сумарну вартість обраних додаткових послуг.
    /// </summary>
    private decimal CalculateAccessoriesCost(List<AccessoryDto> hallAccessories, List<string> selectedAccessories)
    {
        return hallAccessories
            .Where(a => selectedAccessories.Contains(a.Name))
            .Sum(a => a.Price);
    }

    /// <summary>
    /// Генерує зведену аналітичну звітність щодо загальної кількості бронювань, 
    /// сумарного доходу та найпопулярнішого залу.
    /// </summary>
    public AnalyticsSummaryDto GetAnalyticsSummary()
    {
        // Якщо жодного бронювання ще не було, повертаємо базову структуру за замовчуванням
        if (!_bookings.Any())
        {
            return new AnalyticsSummaryDto();
        }

        // Сумуємо дохід з усіх успішних бронювань
        var totalRevenue = _bookings.Sum(b => b.Response.TotalCost);

        // Групуємо за ідентифікатором залу, щоб знайти найпопулярніший за кількістю замовлень
        var popularHallId = _bookings
            .GroupBy(b => b.Request.HallId)
            .OrderByDescending(g => g.Count())
            .First()
            .Key;

        var popularHall = _halls.FirstOrDefault(h => h.Id == popularHallId);

        return new AnalyticsSummaryDto
        {
            TotalBookings = _bookings.Count,
            TotalRevenue = totalRevenue,
            MostPopularHall = popularHall?.Name ?? "Невідомо"
        };
    }

}