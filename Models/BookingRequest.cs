namespace HallRental.API.Models;

public class BookingRequest
{
    public Guid HallId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public List<string> SelectedAccessories { get; set; } = new();
}