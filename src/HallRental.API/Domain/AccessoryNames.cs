namespace HallRental.API.Domain;

/// <summary>
/// The single definition of accessory name identity: " Wi-Fi ", "Wi-Fi" and "wi-fi" are the same service.
/// Names are trimmed before they are stored or matched, then compared ignoring case.
/// </summary>
public static class AccessoryNames
{
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static string Normalize(string? name) => name?.Trim() ?? string.Empty;
}
