namespace HallRental.API.Domain.Errors;

/// <summary>
/// The hall is already booked for an interval overlapping the requested one.
/// The message is safe to show to API clients.
/// </summary>
public sealed class BookingConflictException(Guid hallId)
    : Exception("The hall is already booked for the selected time range.")
{
    public Guid HallId { get; } = hallId;
}
