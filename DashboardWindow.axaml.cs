using Avalonia.Controls;
using Avalonia.Interactivity;
using MySql.Data.MySqlClient;
using System;
using System.Collections.ObjectModel;

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
    public string ImagePath { get; set; } = "";
    public string ImageSource => string.IsNullOrEmpty(ImagePath) ? "avares://Avalon/img/placeholder.jpg" : $"avares://Avalon/img/{ImagePath}";
}

public class MyOrder
{
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
        
        // Сначала ищем ID покупателя, потом грузим данные
        LoadBuyerId();
        LoadEvents();
        LoadProfile();
    }

    private void LoadBuyerId()
    {
        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = "SELECT id FROM ticket_buyers WHERE full_name = @name LIMIT 1";
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", CurrentUserName);
                    var result = cmd.ExecuteScalar();
                    if (result != null) _currentBuyerId = Convert.ToInt32(result);
                    Console.WriteLine($"[DB] ID покупателя: {_currentBuyerId}");
                }
            }
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadBuyerId: {ex.Message}"); }
    }

    private void LoadEvents()
    {
        try
        {
            Events.Clear();
            using (var connection = DatabaseHelper.GetConnection())
            {
                // Упрощенный запрос (без image_path, чтобы избежать ошибки, если колонки нет)
                string query = @"
                    SELECT ce.id, ce.artist_group_name, ce.event_date, ch.hall_name, ce.age_restriction, 
                           ce.parterre_price, ce.vip_price
                    FROM concert_events ce
                    JOIN concert_halls ch ON ce.hall_id = ch.id
                    ORDER BY ce.event_date ASC";

                using (var cmd = new MySqlCommand(query, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Events.Add(new ConcertEvent {
                            Id = reader.GetInt32("id"),
                            ArtistName = reader.GetString("artist_group_name"),
                            EventDate = reader.GetDateTime("event_date"),
                            HallName = reader.GetString("hall_name"),
                            AgeRestriction = reader.IsDBNull(reader.GetOrdinal("age_restriction")) ? "0+" : reader.GetString("age_restriction"),
                            ParterrePrice = reader.GetDecimal("parterre_price"),
                            VipPrice = reader.GetDecimal("vip_price"),
                            ImagePath = "" // Временно пусто
                        });
                    }
                }
            }
            Console.WriteLine($"[DB] Загружено концертов: {Events.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadEvents: {ex.Message}"); }
    }

    private void LoadMyOrders()
    {
        try
        {
            MyOrders.Clear();
            if (_currentBuyerId == 0) { Console.WriteLine("[DB] Нет ID покупателя для загрузки заказов"); return; }

            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = @"
                    SELECT o.order_reference, o.total_amount, o.status, e.artist_group_name, e.event_date
                    FROM ticket_orders o
                    JOIN concert_events e ON o.event_id = e.id
                    WHERE o.buyer_id = @buyerId
                    ORDER BY o.order_datetime DESC";

                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@buyerId", _currentBuyerId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            MyOrders.Add(new MyOrder {
                                OrderRef = reader.GetString("order_reference"),
                                ArtistName = reader.GetString("artist_group_name"),
                                EventDate = reader.GetDateTime("event_date"),
                                TotalAmount = reader.GetDecimal("total_amount"),
                                Status = reader.GetString("status")
                            });
                        }
                    }
                }
            }
            Console.WriteLine($"[DB] Загружено заказов: {MyOrders.Count}");
        }
        catch (Exception ex) { Console.WriteLine($"[DB ERROR] LoadMyOrders: {ex.Message}"); }
    }

    private void LoadProfile()
    {
        try
        {
            if (_currentBuyerId == 0) return;
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = "SELECT full_name, email, phone, has_promo_subscription FROM ticket_buyers WHERE id = @id";
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", _currentBuyerId);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            TxtProfileName.Text = reader.GetString("full_name");
                            TxtProfileEmail.Text = reader.GetString("email");
                            TxtProfilePhone.Text = reader.IsDBNull(reader.GetOrdinal("phone")) ? "Не указан" : reader.GetString("phone");
                            TxtProfilePromo.Text = reader.GetBoolean("has_promo_subscription") ? "Активна" : "Не активна";
                        }
                    }
                }
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
        // Принудительно обновляем данные при переходе
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
        }
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
        using (var conn = DatabaseHelper.GetConnection())
        {
            string query = "UPDATE ticket_buyers SET full_name = @name, email = @email, phone = @phone WHERE id = @id";
            using (var cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@name", TxtProfileName.Text ?? "");
                cmd.Parameters.AddWithValue("@email", TxtProfileEmail.Text ?? "");
                cmd.Parameters.AddWithValue("@phone", TxtProfilePhone.Text ?? "");
                cmd.Parameters.AddWithValue("@id", _currentBuyerId);
                cmd.ExecuteNonQuery();
            }
        }
        TxtProfileMessage.Text = "Профиль успешно обновлен!";
        TxtProfileMessage.IsVisible = true;
        // Обновляем имя в шапке
        CurrentUserName = TxtProfileName.Text ?? "";
        TxtUserName.Text = CurrentUserName;
    }
    catch (Exception ex)
    {
        TxtProfileMessage.Text = $"Ошибка: {ex.Message}";
        TxtProfileMessage.Foreground = Avalonia.Media.Brushes.Red;
        TxtProfileMessage.IsVisible = true;
    }
}
}