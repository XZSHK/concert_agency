using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    public string ImageSource => string.IsNullOrEmpty(ImagePath) ? "avares://Avalon/img/placeholder.jpg" : $"avares://Avalon/img/{ImagePath}";
}

public class MyOrder
{
    public long Id { get; set; }
    public string OrderRef { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
}

public partial class DashboardWindow : Window
{
    public ObservableCollection<ConcertEvent> Events { get; set; } = new();
    public ObservableCollection<MyOrder> MyOrders { get; set; } = new();

    public string CurrentUserName { get; set; } = "";
    private int _currentBuyerId = 0;

    public DashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this;
    }

    public DashboardWindow(string userName) : this()
    {
        CurrentUserName = userName;
        TxtUserName.Text = userName;

        LoadBuyerId();
        LoadEvents();
        LoadProfile();
    }

    private void LoadBuyerId()
    {
        try
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand("SELECT id FROM ticket_buyers WHERE full_name = @name LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@name", CurrentUserName);
            var result = cmd.ExecuteScalar();
            if (result != null) _currentBuyerId = Convert.ToInt32(result);
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadBuyerId: {ex.Message}"); }
    }

    private void LoadEvents()
    {
        try
        {
            Events.Clear();
            using var connection = DatabaseHelper.GetConnection();
            string query = @"
                SELECT ce.id, ce.artist_group_name, ce.event_date, ch.hall_name, ce.age_restriction,
                       ce.parterre_price, ce.vip_price,
                       ch.parterre_capacity, ch.vip_capacity
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC";

            using var cmd = new MySqlCommand(query, connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Events.Add(new ConcertEvent
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
                    ImagePath = ""
                });
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadEvents: {ex.Message}"); }
    }

    private void LoadMyOrders()
    {
        try
        {
            MyOrders.Clear();
            if (_currentBuyerId == 0) return;

            using var conn = DatabaseHelper.GetConnection();
            string query = @"
                SELECT o.id, o.order_reference, o.total_amount, o.status, e.artist_group_name, e.event_date
                FROM ticket_orders o
                JOIN concert_events e ON o.event_id = e.id
                WHERE o.buyer_id = @buyerId
                ORDER BY o.order_datetime DESC";

            using var cmd = new MySqlCommand(query, conn);
            cmd.Parameters.AddWithValue("@buyerId", _currentBuyerId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                MyOrders.Add(new MyOrder
                {
                    Id = reader.GetInt64("id"),
                    OrderRef = reader.GetString("order_reference"),
                    ArtistName = reader.GetString("artist_group_name"),
                    EventDate = reader.GetDateTime("event_date"),
                    TotalAmount = reader.GetDecimal("total_amount"),
                    Status = reader.GetString("status")
                });
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadMyOrders: {ex.Message}"); }
    }

    private void LoadProfile()
    {
        try
        {
            if (_currentBuyerId == 0) return;
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "SELECT full_name, email, phone, has_promo_subscription FROM ticket_buyers WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", _currentBuyerId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                TxtProfileName.Text = reader.GetString("full_name");
                TxtProfileEmail.Text = reader.GetString("email");
                TxtProfilePhone.Text = reader.IsDBNull(reader.GetOrdinal("phone")) ? "Не указан" : reader.GetString("phone");
                TxtProfilePromo.Text = reader.GetBoolean("has_promo_subscription") ? "Активна" : "Не активна";
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadProfile: {ex.Message}"); }
    }

    private void ShowEvents_Click(object? sender, RoutedEventArgs e)
    {
        TxtHeader.Text = "Ближайшие концерты";
        PanelEvents.IsVisible = true;
        PanelMyTickets.IsVisible = false;
        PanelProfile.IsVisible = false;
        LoadEvents();
    }

    private void ShowMyTickets_Click(object? sender, RoutedEventArgs e)
    {
        TxtHeader.Text = "Мои билеты";
        PanelEvents.IsVisible = false;
        PanelMyTickets.IsVisible = true;
        PanelProfile.IsVisible = false;
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

    private void BtnBuyTicket_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ConcertEvent selectedEvent)
        {
            var bookingWindow = new BookingWindow(selectedEvent, CurrentUserName);
            bookingWindow.ShowDialog(this);
            LoadEvents();
            LoadMyOrders();
        }
    }

    // --- Действия с заказами по FSM ---

    private void BtnPayOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MyOrder order) return;
        try
        {
            OrderService.PayAndIssue(order.Id);
            ShowOrdersMessage($"Заказ {order.OrderRef} оплачен и выписан.", true);
            LoadMyOrders();
        }
        catch (Exception ex)
        {
            ShowOrdersMessage(ex.Message, false);
        }
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
        catch (Exception ex)
        {
            ShowOrdersMessage($"Ошибка экспорта: {ex.Message}", false);
        }
    }

    private void BtnCancelOrder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MyOrder order) return;
        try
        {
            OrderService.Refund(order.Id, "Отменено покупателем");
            ShowOrdersMessage($"Заказ {order.OrderRef}: возврат оформлен.", true);
            LoadMyOrders();
        }
        catch (Exception ex)
        {
            // Сюда попадает и запрет возврата < 24 ч из FSM.
            ShowOrdersMessage(ex.Message, false);
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
        TxtOrdersMessage.Foreground = isSuccess ? Brushes.Green : Brushes.Red;
        TxtOrdersMessage.IsVisible = true;
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e)
    {
        var loginWindow = new MainWindow();
        loginWindow.Show();
        this.Close();
    }

    private void BtnSaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "UPDATE ticket_buyers SET full_name = @name, email = @email, phone = @phone WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@name", TxtProfileName.Text ?? "");
            cmd.Parameters.AddWithValue("@email", TxtProfileEmail.Text ?? "");
            cmd.Parameters.AddWithValue("@phone", TxtProfilePhone.Text ?? "");
            cmd.Parameters.AddWithValue("@id", _currentBuyerId);
            cmd.ExecuteNonQuery();

            TxtProfileMessage.Text = "Профиль успешно обновлен!";
            TxtProfileMessage.IsVisible = true;
            CurrentUserName = TxtProfileName.Text ?? "";
            TxtUserName.Text = CurrentUserName;
        }
        catch (Exception ex)
        {
            TxtProfileMessage.Text = $"Ошибка: {ex.Message}";
            TxtProfileMessage.Foreground = Brushes.Red;
            TxtProfileMessage.IsVisible = true;
        }
    }
}
