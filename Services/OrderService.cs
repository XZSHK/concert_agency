using System;
using System.Collections.Generic;
using Avalon.Core;
using MySql.Data.MySqlClient;

namespace Avalon.Services;

/// <summary>
/// Сервисный слой заказов: создание брони, оплата, выписка, контроль входа, возврат.
/// Вся бизнес-логика (суммы, FSM, номер документа) делегируется в Core,
/// здесь только работа с MySQL (транзакции, квотирование мест по секторам).
/// </summary>
public static class OrderService
{
    /// <summary>Мест в одном ряду при автоматической раскладке.</summary>
    public const int SeatsPerRow = 20;

    /// <summary>
    /// Создать заказ в статусе 'Бронь (15 мин)'.
    /// Проверяет квоты зала (партер/VIP), рассчитывает суммы, генерирует номер по маске ТЗ.
    /// </summary>
    public static long CreateBooking(
        int buyerId, int eventId,
        int parterreQty, int vipQty,
        decimal parterrePrice, decimal vipPrice,
        bool hasPromoSubscription,
        int parterreCapacity, int vipCapacity)
    {
        var totals = OrderCalculator.Calculate(
            parterreQty, vipQty, parterrePrice, vipPrice, hasPromoSubscription);

        using var conn = DatabaseHelper.GetConnection();
        using var tx = conn.BeginTransaction();

        // --- Квоты по секторам: занятые места на это событие ---
        int soldParterre = CountSoldSeats(conn, tx, eventId, "Партер");
        int soldVip = CountSoldSeats(conn, tx, eventId, "VIP");

        if (soldParterre + parterreQty > parterreCapacity)
            throw new InvalidOperationException(
                $"Не хватает мест в партере: свободно {Math.Max(0, parterreCapacity - soldParterre)}, запрошено {parterreQty}.");

        if (soldVip + vipQty > vipCapacity)
            throw new InvalidOperationException(
                $"Не хватает мест в VIP: свободно {Math.Max(0, vipCapacity - soldVip)}, запрошено {vipQty}.");

        string orderRef = GenerateNextReference(conn, tx);

        long orderId;
        using (var cmd = new MySqlCommand(
            @"INSERT INTO ticket_orders
                (order_reference, user_id, buyer_id, event_id, order_datetime, service_fee, total_amount, status)
              VALUES (@ref, 1, @buyer, @event, @date, @fee, @total, @status)", conn, tx))
        {
            cmd.Parameters.AddWithValue("@ref", orderRef);
            cmd.Parameters.AddWithValue("@buyer", buyerId);
            cmd.Parameters.AddWithValue("@event", eventId);
            cmd.Parameters.AddWithValue("@date", DateTime.Now);
            cmd.Parameters.AddWithValue("@fee", totals.ServiceFee);
            cmd.Parameters.AddWithValue("@total", totals.Total);
            cmd.Parameters.AddWithValue("@status", OrderStatus.Booking);
            cmd.ExecuteNonQuery();
            orderId = cmd.LastInsertedId;
        }

        InsertSeats(conn, tx, orderId, "Партер", parterreQty, totals.ParterreFinalPrice, soldParterre);
        InsertSeats(conn, tx, orderId, "VIP", vipQty, totals.VipFinalPrice, soldVip);

        tx.Commit();
        return orderId;
    }

    /// <summary>Перевести заказ в новый статус по FSM (с проверкой правила 24 часов для возврата).</summary>
    public static void ChangeStatus(long orderId, string targetStatus, string? cancelReason = null)
    {
        using var conn = DatabaseHelper.GetConnection();

        string currentStatus;
        DateTime eventDate;
        using (var cmd = new MySqlCommand(
            @"SELECT o.status, e.event_date
              FROM ticket_orders o JOIN concert_events e ON e.id = o.event_id
              WHERE o.id = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException($"Заказ #{orderId} не найден.");
            currentStatus = reader.GetString("status");
            eventDate = reader.GetDateTime("event_date");
        }

        // Бросает InvalidOperationException при недопустимом переходе / возврате < 24 ч.
        OrderStateMachine.Transition(currentStatus, targetStatus, eventDate, DateTime.Now);

        using var upd = new MySqlCommand(
            "UPDATE ticket_orders SET status = @status, cancel_reason = @reason WHERE id = @id", conn);
        upd.Parameters.AddWithValue("@status", targetStatus);
        upd.Parameters.AddWithValue("@reason", (object?)cancelReason ?? DBNull.Value);
        upd.Parameters.AddWithValue("@id", orderId);
        upd.ExecuteNonQuery();
    }

    /// <summary>Оплата: Бронь -> Оплачен -> Выписан (полная цепочка ТЗ одним действием).</summary>
    public static void PayAndIssue(long orderId)
    {
        ChangeStatus(orderId, OrderStatus.Paid);
        ChangeStatus(orderId, OrderStatus.Issued);
    }

    /// <summary>Возврат билета (правило 24 часов проверяется в FSM).</summary>
    public static void Refund(long orderId, string reason) =>
        ChangeStatus(orderId, OrderStatus.Refund, reason);

    /// <summary>Контроль на входе: Выписан -> Контроль на входе пройден.</summary>
    public static void PassEntryControl(long orderId) =>
        ChangeStatus(orderId, OrderStatus.EntryPassed);

    /// <summary>Истёк срок брони 15 минут: заказ уходит в 'Возврат билета' с причиной.</summary>
    public static void ExpireBooking(long orderId) =>
        ChangeStatus(orderId, OrderStatus.Refund, "Бронь истекла (15 мин)");

    // ---------- Внутреннее ----------

    private static int CountSoldSeats(MySqlConnection conn, MySqlTransaction tx, int eventId, string sector)
    {
        using var cmd = new MySqlCommand(
            @"SELECT COUNT(*) FROM ticket_order_seats s
              JOIN ticket_orders o ON o.id = s.order_id
              WHERE o.event_id = @event AND s.sector_type = @sector
                AND o.status <> @refund", conn, tx);
        cmd.Parameters.AddWithValue("@event", eventId);
        cmd.Parameters.AddWithValue("@sector", sector);
        cmd.Parameters.AddWithValue("@refund", OrderStatus.Refund);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void InsertSeats(
        MySqlConnection conn, MySqlTransaction tx,
        long orderId, string sector, int qty, decimal finalPrice, int alreadySold)
    {
        for (int i = 0; i < qty; i++)
        {
            int seatNumber = alreadySold + i + 1;
            int rowNumber = (seatNumber - 1) / SeatsPerRow + 1;

            using var cmd = new MySqlCommand(
                @"INSERT INTO ticket_order_seats
                    (order_id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost)
                  VALUES (@oid, @sector, @row, @seat, @base, @final)", conn, tx);
            cmd.Parameters.AddWithValue("@oid", orderId);
            cmd.Parameters.AddWithValue("@sector", sector);
            cmd.Parameters.AddWithValue("@row", rowNumber);
            cmd.Parameters.AddWithValue("@seat", seatNumber);
            // base_seat_price без бейджа; final — с бейджем для VIP.
            cmd.Parameters.AddWithValue("@base", sector == "VIP" ? finalPrice - OrderCalculator.VipBadgeSurcharge : finalPrice);
            cmd.Parameters.AddWithValue("@final", finalPrice);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Следующий номер документа PR23-KTZ-{Год}-{Номер:D5} по БД.</summary>
    public static string GenerateNextReference(MySqlConnection conn, MySqlTransaction? tx = null)
    {
        int year = DateTime.Now.Year;
        int maxSeq = 0;

        using var cmd = new MySqlCommand(
            "SELECT order_reference FROM ticket_orders WHERE order_reference LIKE @mask", conn, tx);
        cmd.Parameters.AddWithValue("@mask", $"PR23-KTZ-{year}-%");

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string? refValue = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (DocumentNumber.TryParse(refValue, out int refYear, out int seq) && refYear == year)
                maxSeq = Math.Max(maxSeq, seq);
        }

        return DocumentNumber.Format(year, DocumentNumber.NextSequence(maxSeq));
    }
}
