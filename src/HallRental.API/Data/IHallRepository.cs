using HallRental.API.Models;

namespace HallRental.API.Data;

/// <summary>
/// Абстракція сховища. Дозволяє HallService працювати однаково
/// з пам'яттю (локально / у тестах) та з Azure SQL (у хмарі).
/// </summary>
public interface IHallRepository
{
    IQueryable<HallDto> Halls { get; }
    IQueryable<Booking> Bookings { get; }

    void AddHall(HallDto hall);
    void RemoveHall(HallDto hall);
    void AddBooking(Booking booking);

    /// <summary>Фіксує зміни (для EF — SaveChanges, для пам'яті — нічого).</summary>
    void SaveChanges();
}
