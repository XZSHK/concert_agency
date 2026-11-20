using System;
using System.Collections.Generic;

namespace Avalon.Core;

/// <summary>
/// Конечный автомат (FSM) жизненного цикла заказа по ТЗ.
///
/// Разрешённая цепочка:
///   'Бронь (15 мин)' -> 'Оплачен' -> 'Выписан' -> 'Контроль на входе пройден'
///   'Бронь (15 мин)' / 'Оплачен' / 'Выписан' -> 'Возврат билета' (если до концерта >= 24 ч)
///
/// Любой недопустимый переход и редактирование закрытого документа
/// бросают <see cref="InvalidOperationException"/> (Тест 4, ПК 1.4).
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<string, HashSet<string>> Allowed = new()
    {
        [OrderStatus.Booking] = new() { OrderStatus.Paid, OrderStatus.Refund },
        [OrderStatus.Paid] = new() { OrderStatus.Issued, OrderStatus.Refund },
        [OrderStatus.Issued] = new() { OrderStatus.EntryPassed, OrderStatus.Refund },
        [OrderStatus.EntryPassed] = new(),
        [OrderStatus.Refund] = new(),
    };

    /// <summary>Можно ли перейти из статуса <paramref name="from"/> в <paramref name="to"/>?</summary>
    public static bool CanTransition(string from, string to)
    {
        if (!OrderStatus.IsValid(from) || !OrderStatus.IsValid(to)) return false;
        return Allowed.TryGetValue(from, out var next) && next.Contains(to);
    }

    /// <summary>Все допустимые следующие статусы (для кнопок в UI).</summary>
    public static IReadOnlyCollection<string> GetNextStatuses(string from)
    {
        if (!OrderStatus.IsValid(from)) return Array.Empty<string>();
        return Allowed[from];
    }

    /// <summary>
    /// Выполнить переход. Бросает <see cref="InvalidOperationException"/> при:
    ///  - недопустимом переходе FSM;
    ///  - возврате менее чем за 24 часа до концерта;
    ///  - попытке перехода из закрытого (терминального) статуса.
    /// </summary>
    /// <param name="currentStatus">Текущий статус заказа.</param>
    /// <param name="targetStatus">Желаемый статус.</param>
    /// <param name="eventDate">Дата концерта (для правила 24 часов).</param>
    /// <param name="now">Текущее время (для тестов передаётся явно).</param>
    public static string Transition(
        string currentStatus, string targetStatus,
        DateTime eventDate, DateTime now)
    {
        if (!OrderStatus.IsValid(currentStatus))
            throw new InvalidOperationException($"Неизвестный текущий статус заказа: '{currentStatus}'.");

        if (!OrderStatus.IsValid(targetStatus))
            throw new InvalidOperationException($"Неизвестный целевой статус заказа: '{targetStatus}'.");

        if (OrderStatus.IsClosed(currentStatus))
            throw new InvalidOperationException(
                $"Документ закрыт (статус '{currentStatus}'). Редактирование и переходы запрещены.");

        if (!CanTransition(currentStatus, targetStatus))
            throw new InvalidOperationException(
                $"Недопустимый переход FSM: '{currentStatus}' -> '{targetStatus}'.");

        if (targetStatus == OrderStatus.Refund)
        {
            TimeSpan untilEvent = eventDate - now;
            if (untilEvent < TimeSpan.FromHours(24))
                throw new InvalidOperationException(
                    "Возврат билетов заблокирован: до даты проведения концерта осталось менее 24 часов.");
        }

        return targetStatus;
    }

    /// <summary>
    /// Проверка перед редактированием документа (мест, сумм).
    /// Закрытый документ редактировать нельзя.
    /// </summary>
    public static void EnsureEditable(string status)
    {
        if (!OrderStatus.IsValid(status))
            throw new InvalidOperationException($"Неизвестный статус заказа: '{status}'.");

        if (OrderStatus.IsClosed(status))
            throw new InvalidOperationException(
                $"Документ закрыт (статус '{status}'). Редактирование запрещено.");
    }
}
