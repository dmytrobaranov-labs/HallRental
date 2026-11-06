using HallRental.API.Domain.Errors;
using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Manages conference halls, their availability and bookings.
/// </summary>
public interface IHallService
{
    /// <summary>
    /// Adds a new conference hall.
    /// </summary>
    /// <param name="hall">Data of the new hall.</param>
    /// <returns>The generated unique id of the hall.</returns>
    /// <exception cref="DomainValidationException">
    /// The name is empty, the capacity is not positive, the price is negative,
    /// or an accessory has an empty name, a negative price or a name repeated ignoring case.
    /// </exception>
    Guid Add(HallDto hall);

    /// <summary>
    /// Returns halls that have at least the minimum capacity
    /// and are not booked for the given time range.
    /// </summary>
    /// <param name="minCapacity">Minimum required hall capacity.</param>
    /// <param name="startTime">Rental start time (optional).</param>
    /// <param name="endTime">Rental end time (optional).</param>
    /// <returns>Halls that are available.</returns>
    IEnumerable<HallDto> GetAvailable(int minCapacity, DateTime? startTime = null, DateTime? endTime = null);

    /// <summary>
    /// Books a hall after checking for time conflicts and calculates the cost using dynamic pricing.
    /// </summary>
    /// <param name="request">Booking request data (hall, time, accessories).</param>
    /// <returns>The booking result with its id and total cost.</returns>
    /// <exception cref="DomainValidationException">The hall id is empty or the end time is not later than the start time.</exception>
    /// <exception cref="HallNotFoundException">The hall does not exist.</exception>
    /// <exception cref="BookingConflictException">The hall is already booked for an overlapping interval.</exception>
    BookingResponse Book(BookingRequest request);

    /// <summary>
    /// Updates an existing hall by its unique id.
    /// </summary>
    /// <param name="id">Unique id of the hall.</param>
    /// <param name="updatedHall">Updated hall data.</param>
    /// <returns>True if the hall was updated; false if the hall was not found.</returns>
    /// <exception cref="DomainValidationException">The updated data breaks the same rules as <see cref="Add"/>.</exception>
    bool Update(Guid id, HallDto updatedHall);

    /// <summary>
    /// Adds a single accessory to the hall's existing list (without removing the others).
    /// </summary>
    /// <param name="hallId">Unique id of the hall.</param>
    /// <param name="accessory">The accessory to add (name and price).</param>
    /// <returns>True if the accessory was added; false if the hall was not found.</returns>
    /// <exception cref="DomainValidationException">The accessory name is empty or its price is negative.</exception>
    bool AddAccessory(Guid hallId, AccessoryDto accessory);

    /// <summary>
    /// Deletes a conference hall by its id.
    /// </summary>
    /// <param name="id">Unique id of the hall.</param>
    /// <returns>True if the hall was deleted; false if the hall was not found.</returns>
    bool Delete(Guid id);

    /// <summary>
    /// Returns the summary business analytics.
    /// </summary>
    AnalyticsSummaryDto GetAnalyticsSummary();
}
