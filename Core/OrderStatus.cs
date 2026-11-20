namespace Avalon.Core;

/// <summary>
/// Статусы заказа из ТЗ (конечный автомат FSM).
/// Строки должны совпадать с БД (ticket_orders.status) и с текстом ТЗ.
/// </summary>
public static class OrderStatus
{
    public const string Booking = "Бронь (15 мин)";
    public const string Paid = "Оплачен";
    public const string Issued = "Выписан";
    public const string EntryPassed = "Контроль на входе пройден";
    public const string Refund = "Возврат билета";

    /// <summary>Терминальные (закрытые) статусы: документ нельзя редактировать.</summary>
    public static bool IsClosed(string status) =>
        status == EntryPassed || status == Refund;

    public static bool IsValid(string status) =>
        status == Booking || status == Paid || status == Issued ||
        status == EntryPassed || status == Refund;
}
