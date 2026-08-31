using HallRental.API.Models;

namespace HallRental.API.Services;

public class HallService : IHallService
{
    private readonly List<HallDto> _halls = new();

    public Guid AddHall(HallDto hall)
    {
        hall.Id = Guid.NewGuid();
        _halls.Add(hall);
        return hall.Id;
    }

    public IEnumerable<HallDto> GetAvailableHalls(int minCapacity)
    {
        return _halls.Where(h => h.Capacity >= minCapacity);
    }

    public BookingResponse BookHall(BookingRequest request)
    {
        var hall = _halls.FirstOrDefault(h => h.Id == request.HallId);
        if (hall == null) throw new Exception("Зал не знайдено");

        var duration = (request.EndTime - request.StartTime).TotalHours;
        if (duration <= 0) throw new Exception("Некоректний час бронювання");

        decimal hallCost = hall.BasePricePerHour * (decimal)duration;

        decimal accessoriesCost = hall.Accessories
            .Where(a => request.SelectedAccessories.Contains(a.Name))
            .Sum(a => a.Price);

        return new BookingResponse
        {
            BookingId = Guid.NewGuid(),
            TotalCost = hallCost + accessoriesCost,
            Message = "Бронювання успішне"
        };
    }
}