using Avalonia.Controls;
using Avalonia.Interactivity;
using MySql.Data.MySqlClient;
using System;
using System.Collections.ObjectModel;

namespace Avalon;

public class OrderData
{
    public int Id { get; set; }
    public string OrderRef { get; set; } = "";
    public string BuyerName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "";
}

public partial class ManagerDashboardWindow : Window
{
    public ObservableCollection<OrderData> Orders { get; set; } = new();

    public ManagerDashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this;
        this.Opened += (s, e) => LoadData();
    }

    public ManagerDashboardWindow(string name) : this()
    {
        TxtUserName.Text = name;
    }

    private void LoadData()
    {
        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = @"
                    SELECT o.id, o.order_reference, o.total_amount, o.status,
                           b.full_name as buyer_name, e.artist_group_name, e.event_date
                    FROM ticket_orders o
                    JOIN ticket_buyers b ON o.buyer_id = b.id
                    JOIN concert_events e ON o.event_id = e.id
                    ORDER BY o.order_datetime DESC";

                using (var cmd = new MySqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    Orders.Clear();
                    while (reader.Read())
                    {
                        Orders.Add(new OrderData {
                            Id = reader.GetInt32("id"),
                            OrderRef = reader.GetString("order_reference"),
                            BuyerName = reader.GetString("buyer_name"),
                            ArtistName = reader.GetString("artist_group_name"),
                            EventDate = reader.GetDateTime("event_date"),
                            TotalAmount = reader.GetDecimal("total_amount"),
                            Status = reader.GetString("status")
                        });
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => LoadData();

    private void BtnRefund_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;

        if ((order.EventDate - DateTime.Now).TotalHours < 24)
        {
            Console.WriteLine("Возврат заблокирован (менее 24 часов до концерта)");
            return;
        }

        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = "UPDATE ticket_orders SET status = 'Возврат билета' WHERE id = @id";
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@id", order.Id);
                    cmd.ExecuteNonQuery();
                }
            }
            LoadData();
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}