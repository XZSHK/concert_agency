using Avalonia.Controls;
using Avalonia.Controls.DataGrid;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using Avalonia.Collections;
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
    private readonly List<OrderData> _allOrders = new();
    private readonly DataGridCollectionView _ordersView;
    private bool _isUpdatingFilter = false;

    public ManagerDashboardWindow()
    {
        InitializeComponent();

        _ordersView = new DataGridCollectionView(_allOrders)
        {
            Filter = ApplyFilter,
        };
        GridOrders.ItemsSource = _ordersView;
        GridOrders.LoadingRow += GridOrders_LoadingRow;
        GridOrders.UnloadingRow += GridOrders_UnloadingRow;

        // Элементы фильтра по статусам (ТЗ: фильтрация по статусам)
        CmbStatusFilter.ItemsSource = new List<string>
        {
            "Все статусы",
            OrderStatus.Booking,
            OrderStatus.Paid,
            OrderStatus.Issued,
            OrderStatus.EntryPassed,
            OrderStatus.Refund,
        };
        CmbStatusFilter.SelectedIndex = 0;

        this.Opened += (s, e) => LoadData();
    }

    public ManagerDashboardWindow(string name) : this()
    {
        TxtUserName.Text = name;
    }

    // ---------- Мгновенная фильтрация (поиск + статус + дата) ----------

    private string? CurrentDateFilter =>
        string.IsNullOrWhiteSpace(TxtFilterDate.Text) ? null : TxtFilterDate.Text.Trim();

    private bool ApplyFilter(object item)
    {
        if (item is not OrderData o) return false;

        // Поиск по номеру / покупателю / концерту
        string search = TxtSearch?.Text?.Trim() ?? "";
        if (search.Length > 0)
        {
            bool matches =
                o.OrderRef.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.BuyerName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                o.ArtistName.Contains(search, StringComparison.OrdinalIgnoreCase);
            if (!matches) return false;
        }

        // Фильтр по статусу
        if (CmbStatusFilter?.SelectedItem is string status && status != "Все статусы" &&
            o.Status != status)
            return false;

        // Фильтр по дате концерта (дд.мм.гггг)
        string? dateFilter = CurrentDateFilter;
        if (!string.IsNullOrWhiteSpace(dateFilter) &&
            InputMasks.TryParseDate(dateFilter, out DateTime filterDate))
        {
            if (o.EventDate.Date != filterDate.Date) return false;
        }

        return true;
    }

    private void RefreshFilters()
    {
        _ordersView.Refresh();
    }

    // ---------- Загрузка данных ----------

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

            _allOrders.Clear();
            while (reader.Read())
            {
                _allOrders.Add(new OrderData
                {
                    Id = reader.GetInt32("id"),
                    OrderRef = reader.GetString("order_reference"),
                    BuyerName = reader.GetString("buyer_name"),
                    ArtistName = reader.GetString("artist_group_name"),
                    EventDate = reader.GetDateTime("event_date"),
                    TotalAmount = reader.GetDecimal("total_amount"),
                    Status = reader.GetString("status"),
                });
            }

            _ordersView.Refresh();
        }
        catch (Exception ex) { ShowMessage($"Ошибка БД: {ex.Message}", false); }
    }

    // ---------- Обработчики панели инструментов ----------

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e) => RefreshFilters();

    private void CmbStatusFilter_SelectionChanged(object? sender, SelectionChangedEventArgs e) => RefreshFilters();

    // Маска даты: дд.мм.гггг (автоточка) + мгновенный фильтр.
    private void TxtFilterDate_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingFilter) return;
        _isUpdatingFilter = true;

        string formatted = InputMasks.FormatDate(TxtFilterDate.Text);
        TxtFilterDate.Text = formatted;
        TxtFilterDate.CaretIndex = formatted.Length;

        _isUpdatingFilter = false;
        RefreshFilters();
    }

    private void BtnClearFilter_Click(object? sender, RoutedEventArgs e)
    {
        TxtFilterDate.Text = "";
        RefreshFilters();
    }

    private void BtnRefresh_Click(object? sender, RoutedEventArgs e) => LoadData();

    // ---------- Цветовая индикация критических состояний ----------

    private static readonly string[] StatusRowClasses = { "order-critical", "order-refund", "order-done" };

    private void GridOrders_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        e.Row.Classes.Remove("order-critical");
        e.Row.Classes.Remove("order-refund");
        e.Row.Classes.Remove("order-done");

        if (e.Row.DataContext is not OrderData order) return;

        switch (order.Status)
        {
            case OrderStatus.Booking: e.Row.Classes.Add("order-critical"); break;   // неоплаченная бронь — критично
            case OrderStatus.Refund: e.Row.Classes.Add("order-refund"); break;      // возврат — выделяем красноватым
            case OrderStatus.EntryPassed: e.Row.Classes.Add("order-done"); break;   // закрытый — зелёным
        }
    }

    private void GridOrders_UnloadingRow(object? sender, DataGridRowEventArgs e)
    {
        foreach (var cls in StatusRowClasses)
            e.Row.Classes.Remove(cls);
    }

    // ---------- Действия по FSM ----------

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

    private async void BtnRefund_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;

        // ТЗ: отмена требует причины длиной не менее 10 символов (модальный диалог).
        var dialog = new CancelReasonDialog();
        string? reason = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(reason)) return;

        RunFsmAction(order, () =>
        {
            OrderService.Refund(order.Id, reason);
            ShowMessage($"Заказ {order.OrderRef}: возврат оформлен.", true);
        });
    }

    private void BtnViewDocument_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;

        var window = new OrderDetailsWindow(order.Id);
        window.ShowDialog(this);
        LoadData();
    }

    private void RunFsmAction(object? sender, Action<OrderData> action)
    {
        if (sender is not Button btn || btn.Tag is not OrderData order) return;
        RunFsmAction(order, () => action(order));
    }

    private void RunFsmAction(OrderData order, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message, false);
        }
        finally
        {
            LoadData();
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
