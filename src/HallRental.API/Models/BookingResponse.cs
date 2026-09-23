namespace HallRental.API.Models;

/// <summary>
/// Результат успішного оформлення бронювання із розрахованою вартістю.
/// </summary>
public class BookingResponse
{
    /// <summary>
    /// Унікальний ідентифікатор підтвердженого бронювання.
    /// </summary>
    public Guid BookingId { get; set; }

    /// <summary>
    /// Загальна розрахована вартість оренди, включаючи обрані послуги (в гривнях).
    /// </summary>
    public decimal TotalCost { get; set; }

    /// <summary>
    /// Статус виконання операції (наприклад, "Бронювання успішне").
    /// </summary>
    public string Message { get; set; } = string.Empty;
}