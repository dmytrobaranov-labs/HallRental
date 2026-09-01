using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Сервіс для управління конференц-залами та обробки бронювань.
/// Реалізує бізнес-логіку, включно з динамічним ціноутворенням.
/// </summary>
public class HallService : IHallService
{
    private readonly List<HallDto> _halls = new();
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
    /// Повертає список залів, місткість яких задовольняє вказаний мінімум.
    /// </summary>
    public IEnumerable<HallDto> GetAvailable(int minCapacity)
    {
        return _halls.Where(h => h.Capacity >= minCapacity);
    }

    /// <summary>
    /// Оформлює бронювання залу з урахуванням часу доби та додаткових послуг.
    /// </summary>
    public BookingResponse Book(BookingRequest request)
    {
        // Знаходимо зал за унікальним ідентифікатором
        var hall = _halls.FirstOrDefault(h => h.Id == request.HallId);
        if (hall == null) throw new Exception("Зал не знайдено");

        // Перевіряємо коректність часового інтервалу бронювання
        ValidateBookingTime(request.StartTime, request.EndTime);

        // Розраховуємо вартість оренди залу з урахуванням динамічних тарифів (знижки/націнки)
        decimal totalHallCost = CalculateTotalHallCost(hall.BasePricePerHour, request.StartTime, request.EndTime);

        // Розраховуємо вартість обраних додаткових послуг
        decimal accessoriesCost = CalculateAccessoriesCost(hall.Accessories, request.SelectedAccessories);

        return new BookingResponse
        {
            BookingId = Guid.NewGuid(),
            TotalCost = Math.Round(totalHallCost + accessoriesCost, 2),
            Message = "Бронювання успішне з урахуванням динамічного тарифу"
        };
    }

    /// <summary>
    /// Оновлює інформацію про існуючий конференц-зал за його ідентифікатором.
    /// </summary>
    public bool Update(Guid id, HallDto updatedHall)
    {
        var hall = _halls.FirstOrDefault(h => h.Id == id);
        if (hall == null) return false;

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
        if (hall == null) return false;

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
    /// Погодинно обчислює загальну вартість оренди залу, застосовуючи правила PricingCalculator.
    /// </summary>
    private decimal CalculateTotalHallCost(decimal basePrice, DateTime start, DateTime end)
    {
        decimal totalCost = 0;
        var currentTime = start;

        while (currentTime < end)
        {
            var nextTime = currentTime.AddHours(1);

            // Якщо остання година неповна, розраховуємо частку від години
            double fraction = nextTime > end ? (end - currentTime).TotalHours : 1.0;

            // Отримуємо тариф для конкретної години доби
            decimal hourlyRate = _pricingCalculator.CalculateHourlyRate(basePrice, currentTime.Hour);
            totalCost += hourlyRate * (decimal)fraction;

            currentTime = nextTime;
        }

        return totalCost;
    }

    /// <summary>
    /// Підраховує сумарну вартість обраних користувачем додаткових послуг.
    /// </summary>
    private decimal CalculateAccessoriesCost(List<AccessoryDto> hallAccessories, List<string> selectedAccessories)
    {
        return hallAccessories
            .Where(a => selectedAccessories.Contains(a.Name))
            .Sum(a => a.Price);
    }
}