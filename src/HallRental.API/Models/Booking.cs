namespace HallRental.API.Models;

/// <summary>
/// Збережене бронювання (сутність БД). Замінює кортеж (BookingRequest, BookingResponse),
/// який раніше зберігався у пам'яті.
/// </summary>
public class Booking
{
    public Guid Id { get; set; }
    public Guid HallId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<string> SelectedAccessories { get; set; } = new();
    public decimal TotalCost { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
