using HallRental.API.Models;

namespace HallRental.API.Services;

public class HallService : IHallService
{
    // Імітація бази даних для зберігання залів у пам'яті
    private readonly List<HallDto> _halls = new();

    public Guid AddHall(HallDto hall)
    {
        // Генеруємо унікальний ідентифікатор для нового залу
        hall.Id = Guid.NewGuid();

        // Зберігаємо зал у список
        _halls.Add(hall);

        // Повертаємо згенерований ID
        return hall.Id;
    }

    public IEnumerable<HallDto> GetAvailableHalls(int minCapacity)
    {
        // Повертаємо зали, місткість яких більша або дорівнює заданій
        return _halls.Where(h => h.Capacity >= minCapacity);
    }
}