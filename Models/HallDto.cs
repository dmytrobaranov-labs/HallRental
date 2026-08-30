namespace HallRental.API.Models;

public class HallDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public decimal BasePricePerHour { get; set; }
    public List<AccessoryDto> Accessories { get; set; } = new();
}