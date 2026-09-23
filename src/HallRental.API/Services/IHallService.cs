using HallRental.API.Models;

namespace HallRental.API.Services;

/// <summary>
/// Інтерфейс сервісу для управління конференц-залами, перевірки їхньої зайнятості та бронювання.
/// </summary>
public interface IHallService
{
    /// <summary>
    /// Додає новий конференц-зал до системи.
    /// </summary>
    /// <param name="hall">Об'єкт із даними нового залу.</param>
    /// <returns>Повертає згенерований унікальний ідентифікатор (Guid) залу.</returns>
    Guid Add(HallDto hall);

    /// <summary>
    /// Повертає список залів, які відповідають мінімальній місткості 
    /// та не заброньовані на вказаний проміжок часу.
    /// </summary>
    /// <param name="minCapacity">Мінімально необхідна місткість залу.</param>
    /// <param name="startTime">Час початку оренди (опціонально).</param>
    /// <param name="endTime">Час завершення оренди (опціонально).</param>
    /// <returns>Колекція вільних конференц-залів.</returns>
    IEnumerable<HallDto> GetAvailable(int minCapacity, DateTime? startTime = null, DateTime? endTime = null);

    /// <summary>
    /// Оформлює бронювання залу з перевіркою часових колізій та динамічним розрахунком вартості.
    /// </summary>
    /// <param name="request">Дані запиту на бронювання (зал, час, послуги).</param>
    /// <returns>Результат бронювання із загальною сумою та ідентифікатором.</returns>
    BookingResponse Book(BookingRequest request);

    /// <summary>
    /// Оновлює інформацію про існуючий зал за його унікальним ідентифікатором.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор залу.</param>
    /// <param name="updatedHall">Оновлені дані залу.</param>
    /// <returns>True, якщо оновлення успішне; False, якщо зал не знайдено.</returns>
    bool Update(Guid id, HallDto updatedHall);

    /// <summary>
    /// Додає одну додаткову послугу до наявного списку послуг залу (без видалення інших).
    /// </summary>
    /// <param name="hallId">Унікальний ідентифікатор залу.</param>
    /// <param name="accessory">Послуга, яку потрібно додати (назва та ціна).</param>
    /// <returns>True, якщо послугу додано; False, якщо зал не знайдено.</returns>
    bool AddAccessory(Guid hallId, AccessoryDto accessory);

    /// <summary>
    /// Видаляє конференц-зал із системи за його ідентифікатором.
    /// </summary>
    /// <param name="id">Унікальний ідентифікатор залу.</param>
    /// <returns>True, якщо видалення успішне; False, якщо зал не знайдено.</returns>
    bool Delete(Guid id);

    /// <summary>
    /// Отримання зведеної бізнес-аналітики.
    /// </summary>
    AnalyticsSummaryDto GetAnalyticsSummary();
}