using HallRental.API.Models;

namespace HallRental.API.Services;

public class HallService : IHallService
{
    private readonly List<HallDto> _halls = new();

    public Guid Add(HallDto hall)
    {
        hall.Id = Guid.NewGuid();
        _halls.Add(hall);
        return hall.Id;
    }

    public IEnumerable<HallDto> GetAvailable(int minCapacity)
    {
        return _halls.Where(h => h.Capacity >= minCapacity);
    }

    public BookingResponse Book(BookingRequest request)
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

    public bool Delete(Guid id)
    {
        var hall = _halls.FirstOrDefault(h => h.Id == id);
        if (hall == null) return false;

        _halls.Remove(hall);
        return true;
    }
}