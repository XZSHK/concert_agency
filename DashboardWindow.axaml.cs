using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalon.Core;
using Avalon.Services;

namespace Avalon;

public class ConcertEvent
{
    public int Id { get; set; }
    public string ArtistName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public string HallName { get; set; } = "";
    public string AgeRestriction { get; set; } = "";
    public decimal ParterrePrice { get; set; }
    public decimal VipPrice { get; set; }
    public int ParterreCapacity { get; set; }
    public int VipCapacity { get; set; }
    public string ImagePath { get; set; } = "";

    /// <summary>Цена VIP с бейджем (base + 500).</summary>
    public decimal VipFinalPrice => OrderCalculator.FinalSeatCost("VIP", VipPrice);

    /// <summary>Картинка карточки: файл из папки img/, либо URL/полный путь, иначе placeholder.
    /// Возвращает готовый Bitmap — Avalonia не конвертирует string в Image.Source сама.</summary>
    public Avalonia.Media.Imaging.Bitmap? ImageSource => ImageSourceHelper.TryLoadBitmap(ImagePath);
}

public class MyOrder
{
    public long Id { get; set; }
    public string OrderRef { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";

    // Доступность действий по FSM (ТЗ): кнопки в таблице включаются/отключаются автоматически.
    public bool CanPay => Status == OrderStatus.Booking;
    public bool CanExport => Status == OrderStatus.Paid || Status == OrderStatus.Issued || Status == OrderStatus.EntryPassed;
    public bool CanCancel => OrderStateMachine.CanRefund(Status, EventDate, DateTime.Now);
}

public partial class DashboardWindow : Window
{
    public ObservableCollection<ConcertEvent> Events { get; set; } = new();
    public ObservableCollection<MyOrder> MyOrders { get; set; } = new();

    public string CurrentUserName { get; set; } = "";

    /// <summary>id покупателя в ticket_buyers; 0 — профиль сотрудника без покупательских функций.</summary>
    public int BuyerId { get; set; }

    private bool _isUpdatingPhone = false;
    private bool _isUpdatingName = false;

    public DashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this;
    }

    public DashboardWindow(int buyerId, string userName) : this()
    {
        BuyerId = buyerId;
        CurrentUserName = userName;

        TxtUserName.Text = userName;
        TxtAvatarInitials.Text = InitialsOf(userName);

        LoadEvents();
        LoadMyOrders();
        LoadProfile();
    }

    private static string InitialsOf(string name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var initials = string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
        return string.IsNullOrEmpty(initials) ? "Я" : initials;
    }

    // ---------- Данные ----------

    private void LoadEvents()
    {
        try
        {
            Events.Clear();
            SetEventsMessage(null);

            using var connection = DatabaseHelper.GetConnection();

            const string baseQuery = @"
                SELECT ce.id, ce.artist_group_name, ce.event_date, ch.hall_name, ce.age_restriction,
                       ce.parterre_price, ce.vip_price,
                       ch.parterre_capacity, ch.vip_capacity,
                       ce.image_path
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC";

            const string legacyQuery = @"
                SELECT ce.id, ce.artist_group_name, ce.event_date, ch.hall_name, ce.age_restriction,
                       ce.parterre_price, ce.vip_price,
                       ch.parterre_capacity, ch.vip_capacity
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC";

            List<ConcertEvent> loaded;
            try
            {
                // Основной запрос: image_path может отсутствовать в старой БД.
                loaded = ReadEvents(connection, baseQuery, includeImage: true);
            }
            catch (MySqlException)
            {
                loaded = ReadEvents(connection, legacyQuery, includeImage: false);
            }

            foreach (var ev in loaded) Events.Add(ev);

            if (Events.Count == 0)
                SetEventsMessage("Афиша пока пуста — загляните позже.");
        }
        catch (Exception ex)
        {
            SetEventsMessage($"Не удалось загрузить афишу: {ex.Message}");
        }
    }

    private static List<ConcertEvent> ReadEvents(MySqlConnection connection, string query, bool includeImage)
    {
        var result = new List<ConcertEvent>();
        using var cmd = new MySqlCommand(query, connection);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ConcertEvent
            {
                Id = reader.GetInt32("id"),
                ArtistName = reader.GetString("artist_group_name"),
                EventDate = reader.GetDateTime("event_date"),
                HallName = reader.GetString("hall_name"),
                AgeRestriction = reader.IsDBNull(reader.GetOrdinal("age_restriction")) ? "0+" : reader.GetString("age_restriction"),
                ParterrePrice = reader.GetDecimal("parterre_price"),
                VipPrice = reader.GetDecimal("vip_price"),
                ParterreCapacity = reader.GetInt32("parterre_capacity"),
                VipCapacity = reader.GetInt32("vip_capacity"),
                ImagePath = includeImage && !reader.IsDBNull(reader.GetOrdinal("image_path"))
                    ? reader.GetString("image_path") : ""
            });
        }
        return result;
    }

    private void LoadMyOrders()
    {
        try
        {
            MyOrders.Clear();
            if (BuyerId == 0)
            {
                ShowOrdersMessage("Раздел доступен только покупателям.", false);
                return;
            }

            using var conn = DatabaseHelper.GetConnection();
            const string query = @"
                SELECT o.id, o.order_reference, o.total_amount, o.status, e.artist_group_name, e.event_date
                FROM ticket_orders o
                JOIN concert_events e ON o.event_id = e.id
                WHERE o.buyer_id = @buyerId
                ORDER BY o.order_datetime DESC";

            using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@buyerId", BuyerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                MyOrders.Add(new MyOrder
                {
                    Id = Convert.ToInt64(reader["id"]),
                    OrderRef = reader.GetString("order_reference"),
                    ArtistName = reader.GetString("artist_group_name"),
                    EventDate = reader.GetDateTime("event_date"),
                    TotalAmount = reader.GetDecimal("total_amount"),
                    Status = reader.GetString("status")
                });
            }
        }
        catch (Exception ex) { ShowOrdersMessage($"Не удалось загрузить заказы: {ex.Message}", false); }
    }

    private void LoadProfile()
    {
        try
        {
            if (BuyerId == 0) return;

            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "SELECT full_name, email, phone, has_promo_subscription FROM ticket_buyers WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", BuyerId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                TxtProfileName.Text = reader.GetString("full_name");
                TxtProfileEmail.Text = reader.GetString("email");
                TxtProfilePhone.Text = reader.IsDBNull(reader.GetOrdinal("phone"))
                    ? "" : reader.GetString("phone");
                TxtProfilePromo.Text = reader.GetBoolean("has_promo_subscription")
                    ? "Активна (сбор 0%)" : "Не активирована";
            }
        }
        catch (Exception ex)
        {
            TxtProfileMessage.Text = $"Не удалось загрузить профиль: {ex.Message}";
            TxtProfileMessage.Foreground = Brushes.Red;
            TxtProfileMessage.IsVisible = true;
        }
    }

    // ---------- Навигация по панелям ----------

    private void ShowEvents_Click(object? sender, RoutedEventArgs e)
    {
        TxtHeader.Text = "Ближайшие концерты";
        PanelEvents.IsVisible = true;
        PanelMyTickets.IsVisible = false;
        PanelProfile.IsVisible = false;
        LoadEvents();
    }

    private void BtnRefreshEvents_Click(object? sender, RoutedEventArgs e) => LoadEvents();

    private void ShowMyTickets_Click(object? sender, RoutedEventArgs e)
    {
        TxtHeader.Text = "Мои билеты";
        PanelEvents.IsVisible = false;
        PanelMyTickets.IsVisible = true;
        PanelProfile.IsVisible = false;
        TxtOrdersMessage.IsVisible = false;
        LoadMyOrders();
    }

    private void ShowProfile_Click(object? sender, RoutedEventArgs e)
    {
        TxtHeader.Text = "Мой профиль";
        PanelEvents.IsVisible = false;
        PanelMyTickets.IsVisible = false;
        PanelProfile.IsVisible = true;
        LoadProfile();
    }

    // ---------- Покупка ----------

    private void BtnBuyTicket_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConcertEvent selectedEvent)
        {
            if (BuyerId == 0)
            {
                SetEventsMessage("Покупка доступна только покупателям. Войдите под покупателем.");
                return;
            }

            var bookingWindow = new BookingWindow(selectedEvent, BuyerId, CurrentUserName);
            bookingWindow.ShowDialog(this);
            LoadEvents();
            LoadMyOrders();
        }
    }

    // ---------- Действия с заказами по FSM ----------

    private void BtnPayOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MyOrder order) return;
        try
        {
            OrderService.PayAndIssue(order.Id);
            ShowOrdersMessage($"Заказ {order.OrderRef} оплачен и выписан.", true);
            LoadMyOrders();
        }
        catch (Exception ex) { ShowOrdersMessage(ex.Message, false); }
    }

    private void BtnExportOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MyOrder order) return;
        try
        {
            var ticket = LoadTicketData(order.Id);
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ArtPrestigeTickets");
            var (txtPath, pngPath, signature) = TicketExportService.Export(ticket, folder);
            ShowOrdersMessage($"Билет сохранён: {txtPath}\nQR: {pngPath}\nSHA256: {signature}", true);
        }
        catch (Exception ex) { ShowOrdersMessage($"Ошибка экспорта: {ex.Message}", false); }
    }

    private async void BtnCancelOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MyOrder order) return;

        // ТЗ: отмена документа требует причины не короче 10 символов (модальный диалог).
        var dialog = new CancelReasonDialog();
        string? reason = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            OrderService.Refund(order.Id, reason);
            ShowOrdersMessage($"Заказ {order.OrderRef}: возврат оформлен.", true);
            LoadMyOrders();
        }
        catch (Exception ex)
        {
            // Сюда попадает и запрет возврата < 24 ч из FSM.
            ShowOrdersMessage(ex.Message, false);
            LoadMyOrders();
        }
    }

    private TicketExportService.TicketData LoadTicketData(long orderId)
    {
        using var conn = DatabaseHelper.GetConnection();
        using var cmd = new MySqlCommand(
            @"SELECT o.order_reference, o.order_datetime, o.total_amount,
                     b.full_name AS buyer_name,
                     e.artist_group_name, e.event_date, e.age_restriction,
                     h.hall_name
              FROM ticket_orders o
              JOIN ticket_buyers b ON b.id = o.buyer_id
              JOIN concert_events e ON e.id = o.event_id
              JOIN concert_halls h ON h.id = e.hall_id
              WHERE o.id = @id", conn);
        cmd.Parameters.AddWithValue("@id", orderId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Заказ не найден.");

        string orderRef = reader.GetString("order_reference");
        DateTime orderDate = reader.GetDateTime("order_datetime");
        decimal total = reader.GetDecimal("total_amount");
        string buyer = reader.GetString("buyer_name");
        string artist = reader.GetString("artist_group_name");
        DateTime eventDate = reader.GetDateTime("event_date");
        string age = reader.IsDBNull(reader.GetOrdinal("age_restriction")) ? "0+" : reader.GetString("age_restriction");
        string hall = reader.GetString("hall_name");
        reader.Close();

        var seats = new List<TicketExportService.SeatLine>();
        using var seatsCmd = new MySqlCommand(
            "SELECT sector_type, row_num, seat_num, final_seat_cost FROM ticket_order_seats WHERE order_id = @id ORDER BY sector_type, row_num, seat_num", conn);
        seatsCmd.Parameters.AddWithValue("@id", orderId);
        using var seatsReader = seatsCmd.ExecuteReader();
        while (seatsReader.Read())
        {
            seats.Add(new TicketExportService.SeatLine(
                seatsReader.GetString("sector_type"),
                seatsReader.GetInt32("row_num"),
                seatsReader.GetInt32("seat_num"),
                seatsReader.GetDecimal("final_seat_cost")));
        }

        return new TicketExportService.TicketData(
            orderRef, orderDate, buyer, artist, hall, eventDate, age, total, seats);
    }

    private void ShowOrdersMessage(string message, bool isSuccess)
    {
        TxtOrdersMessage.Text = message;
        TxtOrdersMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtOrdersMessage.IsVisible = true;
    }

    // ---------- Профиль ----------

    private void TxtProfileName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingName) return;
        _isUpdatingName = true;

        string cleaned = InputMasks.FormatFullName(TxtProfileName.Text);
        if (cleaned != TxtProfileName.Text)
        {
            TxtProfileName.Text = cleaned;
            TxtProfileName.CaretIndex = cleaned.Length;
        }

        _isUpdatingName = false;
    }

    private void TxtProfilePhone_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPhone) return;
        _isUpdatingPhone = true;

        string formatted = InputMasks.FormatPhone(TxtProfilePhone.Text);
        TxtProfilePhone.Text = formatted;
        TxtProfilePhone.CaretIndex = formatted.Length;

        _isUpdatingPhone = false;
    }

    private void BtnSaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        string name = TxtProfileName.Text?.Trim() ?? "";
        string email = TxtProfileEmail.Text?.Trim() ?? "";
        string phone = TxtProfilePhone.Text?.Trim() ?? "";

        if (!InputMasks.IsValidFullName(name))
        {
            SetProfileMessage("Введите корректное ФИО (например: Иванов Иван)", false);
            return;
        }

        if (!InputMasks.IsValidEmail(email))
        {
            SetProfileMessage("Введите корректный Email", false);
            return;
        }

        if (phone.Length > 0 && !InputMasks.IsCompletePhone(phone))
        {
            SetProfileMessage("Телефон должен быть полностью: +7 (999) 123-45-67", false);
            return;
        }

        try
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "UPDATE ticket_buyers SET full_name = @name, email = @email, phone = @phone WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@email", email);
            cmd.Parameters.AddWithValue("@phone", phone.Length == 0 ? DBNull.Value : phone);
            cmd.Parameters.AddWithValue("@id", BuyerId);
            cmd.ExecuteNonQuery();

            CurrentUserName = name;
            TxtUserName.Text = name;
            TxtAvatarInitials.Text = InitialsOf(name);
            SetProfileMessage("Профиль обновлён.", true);
        }
        catch (Exception ex)
        {
            SetProfileMessage($"Ошибка: {ex.Message}", false);
        }
    }

    private void SetProfileMessage(string message, bool isSuccess)
    {
        TxtProfileMessage.Text = message;
        TxtProfileMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtProfileMessage.IsVisible = true;
    }

    private void SetEventsMessage(string? message)
    {
        TxtEventsMessage.Text = message ?? "";
        TxtEventsMessage.IsVisible = !string.IsNullOrEmpty(message);
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e)
    {
        var loginWindow = new MainWindow();
        loginWindow.Show();
        this.Close();
    }
}
