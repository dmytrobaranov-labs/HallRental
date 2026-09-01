namespace HallRental.API.Models;

/// <summary>
/// Модель додаткової послуги або обладнання (наприклад, проєктор, Wi-Fi).
/// </summary>
public class AccessoryDto
{
    /// <summary>
    /// Назва послуги.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Фіксована вартість послуги на весь період оренди (в гривнях).
    /// </summary>
    public decimal Price { get; set; }
}