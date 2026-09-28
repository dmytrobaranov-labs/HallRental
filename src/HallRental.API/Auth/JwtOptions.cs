namespace HallRental.API.Auth;

/// <summary>
/// Налаштування JWT. Локально ключ береться з appsettings.Development.json,
/// в Azure — з Key Vault (через App Service Key Vault reference у змінній Jwt__SigningKey).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "HallRentalAPI";
    public string Audience { get; set; } = "HallRentalClient";
    public string SigningKey { get; set; } = string.Empty;
}
