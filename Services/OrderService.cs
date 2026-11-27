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
    /// <param name="staffUserId">Сотрудник, оформивший заказ, либо null (заказ делает сам покупатель).</param>
    public static long CreateBooking(
        int buyerId, int eventId,
        int parterreQty, int vipQty,
        decimal parterrePrice, decimal vipPrice,
        bool hasPromoSubscription,
        int parterreCapacity, int vipCapacity,
        int? staffUserId = null)
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
        // Онлайн-заказ покупателя оформляется от технического аккаунта с ролью «Клиент»
        // (его создаёт один раз SQL из README); заказ всё равно привязан к покупателю через buyer_id.
        // Заказ из кассы — от сотрудника (staffUserId). Если роль не заведена — вставляем без user_id
        // (сработает только в БД, где столбец допускает пустое значение).
        int? authorId = staffUserId ?? ResolveOnlineAuthorId(conn, tx);
        string insertSql = authorId.HasValue
            ? @"INSERT INTO ticket_orders
                (order_reference, user_id, buyer_id, event_id, order_datetime, service_fee, total_amount, status)
              VALUES (@ref, @user, @buyer, @event, @date, @fee, @total, @status)"
            : @"INSERT INTO ticket_orders
                (order_reference, buyer_id, event_id, order_datetime, service_fee, total_amount, status)
              VALUES (@ref, @buyer, @event, @date, @fee, @total, @status)";
        try
        {
            using var cmd = new MySqlCommand(insertSql, conn, tx);
            cmd.Parameters.AddWithValue("@ref", orderRef);
            if (authorId.HasValue)
                cmd.Parameters.AddWithValue("@user", authorId.Value);
            cmd.Parameters.AddWithValue("@buyer", buyerId);
            cmd.Parameters.AddWithValue("@event", eventId);
            cmd.Parameters.AddWithValue("@date", DateTime.Now);
            cmd.Parameters.AddWithValue("@fee", totals.ServiceFee);
            cmd.Parameters.AddWithValue("@total", totals.Total);
            cmd.Parameters.AddWithValue("@status", OrderStatus.Booking);
            cmd.ExecuteNonQuery();
            orderId = cmd.LastInsertedId;
        }
        catch (MySqlException ex) when (ex.Number == 1048 || ex.Number == 1364 ||
                                        ex.Message.IndexOf("user_id", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            throw new InvalidOperationException(
                "Онлайн-канал не настроен: в БД нет роли «Клиент» / технического аккаунта с ней, " +
                "а столбец ticket_orders.user_id обязателен. Выполните один раз в phpMyAdmin SQL из README (раздел про user_id).", ex);
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

        // ТЗ: запрещено переводить в статус оплаты пустой заказ (0 строк спецификации).
        if (targetStatus == OrderStatus.Paid)
        {
            using var countCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM ticket_order_seats WHERE order_id = @id", conn);
            countCmd.Parameters.AddWithValue("@id", orderId);
            if (Convert.ToInt32(countCmd.ExecuteScalar()) == 0)
                throw new InvalidOperationException(
                    "Нельзя перевести пустой заказ в статус «Оплачен»: в документе нет позиций.");
        }

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


    // ========== Мастер-деталь: заголовок документа и позиции ==========

    /// <summary>Заголовок документа (реквизиты шапки «Документ»).</summary>
    public sealed class OrderHeaderInfo
    {
        public long OrderId { get; init; }
        public string OrderRef { get; init; } = "";
        public DateTime OrderDate { get; init; }
        public string Status { get; init; } = "";
        public string? CancelReason { get; init; }
        public decimal ServiceFee { get; init; }
        public decimal TotalAmount { get; init; }
        public string BuyerName { get; init; } = "";
        public string ArtistName { get; init; } = "";
        public DateTime EventDate { get; init; }
        public string? ResponsibleName { get; init; }
    }

    /// <summary>Позиция документа (строка спецификации).</summary>
    public sealed class OrderSeatLine
    {
        public long SeatId { get; init; }
        public string SectorType { get; init; } = "";
        public int RowNum { get; init; }
        public int SeatNum { get; init; }
        public decimal BaseSeatPrice { get; init; }
        public decimal FinalSeatCost { get; init; }
    }

    /// <summary>Загрузить реквизиты заголовка документа.</summary>
    public static OrderHeaderInfo LoadHeader(long orderId)
    {
        using var conn = DatabaseHelper.GetConnection();
        return LoadHeader(conn, null, orderId);
    }

    private static OrderHeaderInfo LoadHeader(MySqlConnection conn, MySqlTransaction? tx, long orderId)
    {
        const string sql =
            @"SELECT o.order_reference, o.order_datetime, o.status, o.cancel_reason,
                     o.service_fee, o.total_amount,
                     b.full_name AS buyer_name, e.artist_group_name, e.event_date,
                     u.full_name AS responsible_name
              FROM ticket_orders o
              JOIN ticket_buyers b ON b.id = o.buyer_id
              JOIN concert_events e ON e.id = o.event_id
              LEFT JOIN users u ON u.id = o.user_id
              WHERE o.id = @id";
        using var cmd = tx is null ? new MySqlCommand(sql, conn) : new MySqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("@id", orderId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Заказ #{orderId} не найден.");

        return new OrderHeaderInfo
        {
            OrderId = orderId,
            OrderRef = reader.GetString("order_reference"),
            OrderDate = reader.GetDateTime("order_datetime"),
            Status = reader.GetString("status"),
            CancelReason = reader.IsDBNull(reader.GetOrdinal("cancel_reason")) ? null : reader.GetString("cancel_reason"),
            ServiceFee = reader.GetDecimal("service_fee"),
            TotalAmount = reader.GetDecimal("total_amount"),
            BuyerName = reader.GetString("buyer_name"),
            ArtistName = reader.GetString("artist_group_name"),
            EventDate = reader.GetDateTime("event_date"),
            ResponsibleName = reader.IsDBNull(reader.GetOrdinal("responsible_name")) ? null : reader.GetString("responsible_name"),
        };
    }

    /// <summary>Загрузить позиции документа (строки спецификации).</summary>
    public static List<OrderSeatLine> LoadSeats(long orderId)
    {
        using var conn = DatabaseHelper.GetConnection();
        using var cmd = new MySqlCommand(
            @"SELECT id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost
              FROM ticket_order_seats
              WHERE order_id = @id
              ORDER BY sector_type, row_num, seat_num", conn);
        cmd.Parameters.AddWithValue("@id", orderId);

        var lines = new List<OrderSeatLine>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            lines.Add(new OrderSeatLine
            {
                SeatId = Convert.ToInt64(reader["id"]),
                SectorType = reader.GetString("sector_type"),
                RowNum = reader.GetInt32("row_num"),
                SeatNum = reader.GetInt32("seat_num"),
                BaseSeatPrice = reader.GetDecimal("base_seat_price"),
                FinalSeatCost = reader.GetDecimal("final_seat_cost"),
            });
        }
        return lines;
    }

    /// <summary>Добавить одну позицию в документ в статусе «Бронь (15 мин)».</summary>
    public static void AddSeat(long orderId, string sector)
    {
        if (sector != "Партер" && sector != "VIP")
            throw new ArgumentException("Сектор должен быть «Партер» или «VIP».", nameof(sector));

        using var conn = DatabaseHelper.GetConnection();
        using var tx = conn.BeginTransaction();

        var header = LoadHeader(conn, tx, orderId);
        EnsureBooking(header.Status);

        // Цены — из события; вместимость — из зала.
        decimal parterrePrice, vipPrice; int parterreCapacity, vipCapacity; int eventId;
        using (var cmd = new MySqlCommand(
            @"SELECT e.id, e.parterre_price, e.vip_price,
                     COALESCE(h.parterre_capacity, 0) AS parterre_capacity,
                     COALESCE(h.vip_capacity, 0) AS vip_capacity
              FROM ticket_orders o
              JOIN concert_events e ON e.id = o.event_id
              JOIN concert_halls h ON h.id = e.hall_id
              WHERE o.id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException($"Заказ #{orderId} не найден.");
            eventId = reader.GetInt32("id");
            parterrePrice = reader.GetDecimal("parterre_price");
            vipPrice = reader.GetDecimal("vip_price");
            parterreCapacity = reader.GetInt32("parterre_capacity");
            vipCapacity = reader.GetInt32("vip_capacity");
        }

        int alreadySold = CountSoldSeats(conn, tx, eventId, sector);
        int capacity = sector == "VIP" ? vipCapacity : parterreCapacity;
        if (alreadySold + 1 > capacity)
            throw new InvalidOperationException(
                $"Не хватает мест в секторе «{sector}»: свободно {Math.Max(0, capacity - alreadySold)}.");

        decimal basePrice = sector == "VIP" ? vipPrice : parterrePrice;
        decimal finalPrice = OrderCalculator.FinalSeatCost(sector, basePrice);
        int seatNumber = alreadySold + 1;
        int rowNumber = (seatNumber - 1) / SeatsPerRow + 1;

        using (var cmd = new MySqlCommand(
            @"INSERT INTO ticket_order_seats
                (order_id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost)
              VALUES (@oid, @sector, @row, @seat, @base, @final)", conn, tx))
        {
            cmd.Parameters.AddWithValue("@oid", orderId);
            cmd.Parameters.AddWithValue("@sector", sector);
            cmd.Parameters.AddWithValue("@row", rowNumber);
            cmd.Parameters.AddWithValue("@seat", seatNumber);
            cmd.Parameters.AddWithValue("@base", basePrice);
            cmd.Parameters.AddWithValue("@final", finalPrice);
            cmd.ExecuteNonQuery();
        }

        RecomputeTotals(conn, tx, orderId);
        tx.Commit();
    }

    /// <summary>Удалить позицию из документа в статусе «Бронь (15 мин)».</summary>
    /// <returns>Id заказа, к которому относилась позиция.</returns>
    public static long RemoveSeat(long seatId)
    {
        using var conn = DatabaseHelper.GetConnection();
        using var tx = conn.BeginTransaction();

        long orderId;
        string status;
        using (var cmd = new MySqlCommand(
            @"SELECT s.order_id, o.status
              FROM ticket_order_seats s JOIN ticket_orders o ON o.id = s.order_id
              WHERE s.id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", seatId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                throw new InvalidOperationException("Позиция не найдена.");
            orderId = Convert.ToInt64(reader["order_id"]);
            status = reader.GetString("status");
        }

        EnsureBooking(status);

        using (var cmd = new MySqlCommand(
            "DELETE FROM ticket_order_seats WHERE id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", seatId);
            cmd.ExecuteNonQuery();
        }

        RecomputeTotals(conn, tx, orderId);
        tx.Commit();
        return orderId;
    }

    private static void EnsureBooking(string status)
    {
        if (!string.Equals(status, OrderStatus.Booking, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Редактирование позиций разрешено только в статусе «{OrderStatus.Booking}». " +
                $"Оплаченные и закрытые документы изменению не подлежат (текущий статус: «{status}»).");
    }

    // Пересчёт итогов документа на лету: сумма позиций + сервисный сбор (промокод обнуляет сбор).
    private static void RecomputeTotals(MySqlConnection conn, MySqlTransaction tx, long orderId)
    {
        decimal seatsTotal;
        using (var cmd = new MySqlCommand(
            @"SELECT COALESCE(SUM(final_seat_cost), 0) FROM ticket_order_seats WHERE order_id = @id",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            seatsTotal = Convert.ToDecimal(cmd.ExecuteScalar());
        }

        bool hasPromo = false;
        using (var cmd = new MySqlCommand(
            @"SELECT COALESCE(b.has_promo_subscription, 0)
              FROM ticket_orders o LEFT JOIN ticket_buyers b ON b.id = o.buyer_id
              WHERE o.id = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("@id", orderId);
            hasPromo = Convert.ToBoolean(cmd.ExecuteScalar());
        }

        decimal fee = hasPromo
            ? 0m
            : Math.Round(seatsTotal * OrderCalculator.ServiceFeeRate, 2, MidpointRounding.AwayFromZero);

        using var upd = new MySqlCommand(
            "UPDATE ticket_orders SET service_fee = @fee, total_amount = @total WHERE id = @id",
            conn, tx);
        upd.Parameters.AddWithValue("@fee", fee);
        upd.Parameters.AddWithValue("@total", seatsTotal + fee);
        upd.Parameters.AddWithValue("@id", orderId);
        upd.ExecuteNonQuery();
    }

    // Id технического аккаунта онлайн-канала (роль «Клиент») либо null, если роль не заведена.
    private static int? ResolveOnlineAuthorId(MySqlConnection conn, MySqlTransaction tx)
    {
        using var cmd = new MySqlCommand(
            @"SELECT u.id FROM users u JOIN roles r ON r.id = u.role_id
              WHERE r.name = 'Клиент' ORDER BY u.id LIMIT 1", conn, tx);
        var value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

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
