namespace HallRental.API.Models;

public class BookingResponse
{
    public Guid BookingId { get; set; }
    public decimal TotalCost { get; set; }
    public string Message { get; set; } = string.Empty;
}