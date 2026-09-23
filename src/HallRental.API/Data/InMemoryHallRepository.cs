using HallRental.API.Models;

namespace HallRental.API.Data;

/// <summary>
/// Сховище у пам'яті — використовується, коли рядок підключення до БД не задано
/// (локальна розробка, модульні та інтеграційні тести).
/// </summary>
public class InMemoryHallRepository : IHallRepository
{
    private readonly List<HallDto> _halls = new();
    private readonly List<Booking> _bookings = new();

    public IQueryable<HallDto> Halls => _halls.AsQueryable();
    public IQueryable<Booking> Bookings => _bookings.AsQueryable();

    public void AddHall(HallDto hall) => _halls.Add(hall);
    public void RemoveHall(HallDto hall) => _halls.Remove(hall);
    public void AddBooking(Booking booking) => _bookings.Add(booking);
    public void SaveChanges() { }
}
