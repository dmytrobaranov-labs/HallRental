using HallRental.API.Models;

namespace HallRental.API.Services;

public interface IHallService
{
    Guid AddHall(HallDto hall);
    IEnumerable<HallDto> GetAvailableHalls(int minCapacity);

    public BookingResponse BookHall(BookingRequest request);
}