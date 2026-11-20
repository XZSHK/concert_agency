using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.ObjectModel;
using Avalon.Core;
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

    // Доступность действий по FSM (кнопки в таблице включены ровно тогда, когда переход легален).
    public bool CanPay => Status == OrderStatus.Booking;
    public bool CanIssue => Status == OrderStatus.Paid;
    public bool CanControl => Status == OrderStatus.Issued;
    public bool CanRefund => OrderStateMachine.CanRefund(Status, EventDate, DateTime.Now);
}

public partial class ManagerDashboardWindow : Window
{
    public ObservableCollection<OrderData> Orders { get; set; } = new();

    private bool _isUpdatingFilter = false;

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

    /// <summary>Загрузка заказов; если задан фильтр — только заказы на концерты этой даты.</summary>
    private void LoadData(string? dateFilter = null)
    {
        try
        {
            using var conn = DatabaseHelper.GetConnection();

            string query = @"
                SELECT o.id, o.order_reference, o.total_amount, o.status,
                       b.full_name as buyer_name, e.artist_group_name, e.event_date
                FROM ticket_orders o
                JOIN ticket_buyers b ON o.buyer_id = b.id
                JOIN concert_events e ON o.event_id = e.id";

            bool hasFilter = false;
            DateTime filterDate = default;
            if (!string.IsNullOrWhiteSpace(dateFilter) &&
                InputMasks.TryParseDate(dateFilter, out filterDate))
            {
                query += " WHERE DATE(e.event_date) = @filterDate";
                hasFilter = true;
            }

            query += " ORDER BY o.order_datetime DESC";

            using var cmd = new MySqlCommand(query, conn);
            if (hasFilter)
                cmd.Parameters.AddWithValue("@filterDate", filterDate.Date);

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

    private string? CurrentFilter =>
        string.IsNullOrWhiteSpace(TxtFilterDate.Text) ? null : TxtFilterDate.Text.Trim();

    // Маска даты: дд.мм.гггг (автоточка).
    private void TxtFilterDate_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFilter) return;
        _isUpdatingFilter = true;

        string formatted = InputMasks.FormatDate(TxtFilterDate.Text);
        TxtFilterDate.Text = formatted;
        TxtFilterDate.CaretIndex = formatted.Length;

        _isUpdatingFilter = false;

        // Автоматически применяем фильтр, когда дата введена полностью.
        if (formatted.Length == 10)
            LoadData(formatted);
        else if (formatted.Length == 0)
            LoadData();
    }

    private void BtnClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        TxtFilterDate.Text = "";
        LoadData();
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => LoadData(CurrentFilter);

    // --- Переходы FSM для менеджера ---

    private void BtnPay_Click(object? sender, RoutedEventArgs e) =>
        RunFsmAction(sender, order =>
        {
            OrderService.ChangeStatus(order.Id, OrderStatus.Paid);
            ShowMessage($"Заказ {order.OrderRef}: статус 'Оплачен'.", true);
        });

    private void BtnIssue_Click(object? sender, RoutedEventArgs e) =>
        RunFsmAction(sender, order =>
        {
            OrderService.ChangeStatus(order.Id, OrderStatus.Issued);
            ShowMessage($"Заказ {order.OrderRef}: билет выписан.", true);
        });

    private void BtnEntryControl_Click(object? sender, RoutedEventArgs e) =>
        RunFsmAction(sender, order =>
        {
            OrderService.PassEntryControl(order.Id);
            ShowMessage($"Заказ {order.OrderRef}: контроль на входе пройден.", true);
        });

    private void BtnRefund_Click(object? sender, RoutedEventArgs e) =>
        RunFsmAction(sender, order =>
        {
            // Правило ТЗ (возврат < 24 ч запрещён) и проверка FSM — внутри OrderService/OrderStateMachine.
            OrderService.Refund(order.Id, "Возврат оформлен менеджером");
            ShowMessage($"Заказ {order.OrderRef}: возврат оформлен.", true);
        });

    private void RunFsmAction(object? sender, Action<OrderData> action)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;
        try
        {
            action(order);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, false);
        }
        finally
        {
            LoadData(CurrentFilter);
        }
    }

    private void ShowMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtMessage.IsVisible = true;
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}
