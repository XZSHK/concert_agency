using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.ObjectModel;
using Avalon.Services;

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
            using var conn = DatabaseHelper.GetConnection();
            string query = @"
                SELECT o.id, o.order_reference, o.total_amount, o.status,
                       b.full_name as buyer_name, e.artist_group_name, e.event_date
                FROM ticket_orders o
                JOIN ticket_buyers b ON o.buyer_id = b.id
                JOIN concert_events e ON o.event_id = e.id
                ORDER BY o.order_datetime DESC";

            using var cmd = new MySqlCommand(query, conn);
            using var reader = cmd.ExecuteReader();
            Orders.Clear();
            while (reader.Read())
            {
                Orders.Add(new OrderData
                {
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
        catch (Exception ex) { ShowMessage($"Ошибка БД: {ex.Message}", false); }
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => LoadData();

    // --- Переходы FSM для менеджера ---

    private void BtnPay_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;
        try
        {
            OrderService.ChangeStatus(order.Id, Avalon.Core.OrderStatus.Paid);
            ShowMessage($"Заказ {order.OrderRef}: статус 'Оплачен'.", true);
            LoadData();
        }
        catch (Exception ex) { ShowMessage(ex.Message, false); }
    }

    private void BtnIssue_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;
        try
        {
            OrderService.ChangeStatus(order.Id, Avalon.Core.OrderStatus.Issued);
            ShowMessage($"Заказ {order.OrderRef}: билет выписан.", true);
            LoadData();
        }
        catch (Exception ex) { ShowMessage(ex.Message, false); }
    }

    private void BtnEntryControl_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;
        try
        {
            OrderService.PassEntryControl(order.Id);
            ShowMessage($"Заказ {order.OrderRef}: контроль на входе пройден.", true);
            LoadData();
        }
        catch (Exception ex) { ShowMessage(ex.Message, false); }
    }

    private void BtnRefund_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;

        try
        {
            // Правило ТЗ (возврат < 24 ч запрещён) и проверка FSM — внутри OrderService/OrderStateMachine.
            OrderService.Refund(order.Id, "Возврат оформлен менеджером");
            ShowMessage($"Заказ {order.OrderRef}: возврат оформлен.", true);
            LoadData();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, false);
        }
    }

    private void ShowMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Brushes.Green : Brushes.Red;
        TxtMessage.IsVisible = true;
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}
