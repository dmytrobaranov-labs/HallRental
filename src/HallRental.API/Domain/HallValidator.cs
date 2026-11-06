using HallRental.API.Domain.Errors;
using HallRental.API.Models;

namespace HallRental.API.Domain;

/// <summary>
/// Business rules for hall data, shared by every operation that writes a hall
/// so that creating and updating can never accept different data.
/// </summary>
public static class HallValidator
{
    /// <exception cref="DomainValidationException">The hall or one of its accessories breaks a rule.</exception>
    public static void Validate(HallDto hall)
    {
        if (string.IsNullOrWhiteSpace(hall.Name))
            throw new DomainValidationException("Hall name must not be empty.");

        if (hall.Capacity <= 0)
            throw new DomainValidationException("Hall capacity must be greater than zero.");

        if (hall.BasePricePerHour < 0)
            throw new DomainValidationException("Base price must not be negative.");

        // JSON "accessories": null bypasses the property initializer.
        if (hall.Accessories is null)
            throw new DomainValidationException("Accessories must be a list (use [] for none).");

        foreach (var accessory in hall.Accessories)
            ValidateAccessory(accessory);

        EnsureUniqueAccessoryNames(hall.Accessories);
    }

    /// <exception cref="DomainValidationException">The accessory name is empty or its price is negative.</exception>
    public static void ValidateAccessory(AccessoryDto? accessory)
    {
        if (accessory is null)
            throw new DomainValidationException("Accessory must not be null.");

        if (string.IsNullOrWhiteSpace(accessory.Name))
            throw new DomainValidationException("Accessory name must not be empty.");

        if (accessory.Price < 0)
            throw new DomainValidationException($"Price of accessory '{accessory.Name}' must not be negative.");
    }

    private static void EnsureUniqueAccessoryNames(IEnumerable<AccessoryDto> accessories)
    {
        var duplicateNames = accessories
            .GroupBy(a => AccessoryNames.Normalize(a.Name), AccessoryNames.Comparer)
            .Where(group => group.Count() > 1)
            .Select(group => $"'{group.Key}'")
            .ToList();

        if (duplicateNames.Count > 0)
            throw new DomainValidationException(
                $"Accessory names must be unique (case-insensitive). Duplicates: {string.Join(", ", duplicateNames)}.");
    }
}
