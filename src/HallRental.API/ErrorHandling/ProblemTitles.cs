namespace HallRental.API.ErrorHandling;

/// <summary>
/// ProblemDetails titles shared by the exception handler and endpoints that return problems directly,
/// so the same kind of error always looks the same to clients.
/// </summary>
public static class ProblemTitles
{
    public const string Validation = "Validation failed";
    public const string BadRequest = "Bad request";
    public const string HallNotFound = "Hall not found";
    public const string BookingConflict = "Booking conflict";
    public const string Unexpected = "An unexpected error occurred";
}
