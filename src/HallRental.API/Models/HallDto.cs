namespace HallRental.API.Models;

/// <summary>
/// Модель даних конференц-залу.
/// </summary>
public class HallDto
{
    /// <summary>
    /// Унікальний ідентифікатор залу.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Назва залу (наприклад, "Зал А").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Місткість залу (максимальна кількість осіб).
    /// </summary>
    public int Capacity { get; set; }

    /// <summary>
    /// Базова вартість оренди залу за одну годину (в гривнях).
    /// </summary>
    public decimal BasePricePerHour { get; set; }

    /// <summary>
    /// Список доступних додаткових послуг (обладнання) для цього залу.
    /// </summary>
    public List<AccessoryDto> Accessories { get; set; } = new();
}