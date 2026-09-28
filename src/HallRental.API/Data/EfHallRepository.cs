using HallRental.API.Models;

namespace HallRental.API.Data;

/// <summary>
/// Сховище на основі EF Core (Azure SQL).
/// </summary>
public class EfHallRepository(HallRentalDbContext db) : IHallRepository
{
    public IQueryable<HallDto> Halls => db.Halls;
    public IQueryable<Booking> Bookings => db.Bookings;

    public void AddHall(HallDto hall) => db.Halls.Add(hall);
    public void RemoveHall(HallDto hall) => db.Halls.Remove(hall);
    public void AddBooking(Booking booking) => db.Bookings.Add(booking);
    public void SaveChanges() => db.SaveChanges();
}
