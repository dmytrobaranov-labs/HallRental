namespace HallRental.API.Domain.Errors;

/// <summary>
/// The request breaks a business rule (e.g. empty name, end time not after start time).
/// The message is safe to show to API clients.
/// </summary>
public sealed class ValidationException(string message) : Exception(message);
