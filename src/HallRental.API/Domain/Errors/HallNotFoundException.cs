namespace HallRental.API.Domain.Errors;

/// <summary>
/// The requested hall does not exist. The message is safe to show to API clients.
/// </summary>
public sealed class HallNotFoundException(Guid hallId) : Exception(CreateMessage(hallId))
{
    public Guid HallId { get; } = hallId;

    private static string CreateMessage(Guid hallId) => $"Hall '{hallId}' was not found.";
}
