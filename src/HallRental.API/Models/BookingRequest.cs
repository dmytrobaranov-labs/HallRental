namespace HallRental.API.Models;

/// <summary>
/// Запит на створення нового бронювання
/// </summary>
public class BookingRequest
{
    /// <summary>
    /// Унікальний ідентифікатор конференц-залу
    /// </summary>
    public Guid HallId { get; set; }

    /// <summary>
    /// Дата та час початку бронювання
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Дата та час завершення бронювання
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Список назв додаткових послуг (наприклад: "Проєктор", "Wi-Fi")
    /// </summary>
    public List<string> SelectedAccessories { get; set; } = new();
}