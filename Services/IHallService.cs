using HallRental.API.Models;

namespace HallRental.API.Services;

public interface IHallService
{
    Guid Add(HallDto hall);
    IEnumerable<HallDto> GetAvailable(int minCapacity);
    BookingResponse Book(BookingRequest request);
    bool Update(Guid id, HallDto updatedHall);
    bool Delete(Guid id);
}