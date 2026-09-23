namespace HallRental.API.Models;

/// <summary>
/// Зведена статистична інформація про роботу системи бронювання.
/// </summary>
public class AnalyticsSummaryDto
{
    /// <summary>
    /// Загальна кількість успішних бронювань.
    /// </summary>
    public int TotalBookings { get; set; }

    /// <summary>
    /// Загальний дохід від оренди залів та послуг (у гривнях).
    /// </summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>
    /// Назва найпопулярнішого залу за кількістю бронювань.
    /// </summary>
    public string MostPopularHall { get; set; } = "Відсутні";
}