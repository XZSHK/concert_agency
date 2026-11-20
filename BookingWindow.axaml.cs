using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using Avalon.Core;
using Avalon.Services;

namespace Avalon;

public partial class BookingWindow : Window
{
    private ConcertEvent? _currentEvent;
    private string _userName = "";
    private int _buyerId = 0;
    private int _parterreQty = 0;
    private int _vipQty = 0;
    private bool _hasPromoSubscription = false;

    // Бронь: заказ создан, но ещё не оплачен.
    private long? _orderId;
    private string _orderRef = "";
    private bool _isPaid;
    private bool _isExpired;

    // Таймер удержания билета 15 минут (ТЗ: DispatcherTimer).
    private DispatcherTimer? _timer;
    private TimeSpan _timeLeft = TimeSpan.FromMinutes(15);

    public BookingWindow()
    {
        InitializeComponent();
        this.Closed += (_, _) => OnWindowClosed();
    }

    public BookingWindow(ConcertEvent selectedEvent, int buyerId, string userName) : this()
    {
        _currentEvent = selectedEvent;
        _buyerId = buyerId;
        _userName = userName;

        this.DataContext = _currentEvent;

        TxtConcertName.Text = _currentEvent.ArtistName;
        TxtConcertInfo.Text = $"{_currentEvent.EventDate:dd MMMM yyyy, HH:mm}  ·  {_currentEvent.HallName}";

        LoadBuyerSubscription();
        UpdatePrices();
    }

    private void LoadBuyerSubscription()
    {
        try
        {
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "SELECT has_promo_subscription FROM ticket_buyers WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", _buyerId);
            var result = cmd.ExecuteScalar();
            if (result != null && result != DBNull.Value)
                _hasPromoSubscription = Convert.ToBoolean(result);
        }
        catch (Exception ex) { ShowMessage($"Ошибка БД: {ex.Message}", false); }
    }

    private void UpdatePrices()
    {
        if (_currentEvent == null) return;

        TxtParterrePrice.Text = $"{_currentEvent.ParterrePrice:N0} ₽ / шт";
        TxtVipPrice.Text = $"{OrderCalculator.FinalSeatCost("VIP", _currentEvent.VipPrice):N0} ₽ / шт (бейдж включён)";

        if (_parterreQty == 0 && _vipQty == 0)
        {
            TxtBasePrice.Text = "0 ₽";
            TxtServiceFee.Text = "0 ₽";
            TxtServiceFee.Foreground = Brushes.White;
            TxtTotal.Text = "0 ₽";
            return;
        }

        // Суммы считает OrderCalculator (формула ТЗ), окно только отображает.
        var totals = OrderCalculator.Calculate(
            _parterreQty, _vipQty,
            _currentEvent.ParterrePrice, _currentEvent.VipPrice,
            _hasPromoSubscription);

        TxtBasePrice.Text = $"{totals.Subtotal:N0} ₽";
        TxtServiceFee.Text = _hasPromoSubscription
            ? "0 ₽ (промокод)"
            : $"{totals.ServiceFee:N0} ₽";
        TxtServiceFee.Foreground = _hasPromoSubscription ? Brushes.LightGreen : Brushes.White;
        TxtTotal.Text = $"{totals.Total:N0} ₽";
    }

    private void BtnPlusParterre_Click(object? sender, RoutedEventArgs e) { _parterreQty++; RefreshQty(); }
    private void BtnMinusParterre_Click(object? sender, RoutedEventArgs e) { if (_parterreQty > 0) _parterreQty--; RefreshQty(); }
    private void BtnPlusVip_Click(object? sender, RoutedEventArgs e) { _vipQty++; RefreshQty(); }
    private void BtnMinusVip_Click(object? sender, RoutedEventArgs e) { if (_vipQty > 0) _vipQty--; RefreshQty(); }

    private void RefreshQty()
    {
        TxtParterreQty.Text = _parterreQty.ToString();
        TxtVipQty.Text = _vipQty.ToString();
        UpdatePrices();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        ExpireUnpaidBooking();
        this.Close();
    }

    private void OnWindowClosed()
    {
        // Крестик в заголовке = отказ от неоплаченной брони: места освобождаются.
        StopTimer();
        ExpireUnpaidBooking();
    }

    private void ExpireUnpaidBooking()
    {
        if (_orderId.HasValue && !_isPaid)
        {
            try { OrderService.ExpireBooking(_orderId.Value); }
            catch { /* заказ уже закрыт */ }
        }
    }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEvent == null) return;
        if (_parterreQty == 0 && _vipQty == 0) { ShowMessage("Выберите хотя бы один билет!", false); return; }
        if (_buyerId == 0) { ShowMessage("Покупатель не определён. Войдите заново.", false); return; }
        if (_orderId.HasValue) { ShowMessage("Заказ уже оформлен. У вас есть 15 минут на оплату.", false); return; }

        try
        {
            _orderId = OrderService.CreateBooking(
                _buyerId, _currentEvent.Id,
                _parterreQty, _vipQty,
                _currentEvent.ParterrePrice, _currentEvent.VipPrice,
                _hasPromoSubscription,
                _currentEvent.ParterreCapacity, _currentEvent.VipCapacity);

            _isExpired = false;
            _orderRef = LoadOrderReference(_orderId.Value);
            TxtBookingStatus.Text = $"{OrderStatus.Booking} · {_orderRef}";
            TxtBookingStatus.Foreground = Brushes.White;
            BtnConfirm.IsEnabled = false;
            BtnPay.IsEnabled = true;
            ShowMessage($"Заказ {_orderRef} оформлен. У вас 15 минут на оплату.", true);

            StartTimer();
        }
        catch (InvalidOperationException ex) { ShowMessage(ex.Message, false); }
        catch (Exception ex) { ShowMessage($"Ошибка БД: {ex.Message}", false); }
    }

    private void BtnPay_Click(object? sender, RoutedEventArgs e)
    {
        if (!_orderId.HasValue) return;

        if (_isExpired)
        {
            ShowMessage("Время брони истекло. Оформите заказ заново.", false);
            return;
        }

        try
        {
            // Цепочка FSM: Бронь -> Оплачен -> Выписан.
            OrderService.PayAndIssue(_orderId.Value);
            _isPaid = true;
            StopTimer();
            TxtTimer.Text = "Оплачено";
            TxtTimer.Foreground = Brushes.LightGreen;
            TxtBookingStatus.Text = OrderStatus.Issued;
            TxtBookingStatus.Foreground = Brushes.LightGreen;
            BtnPay.IsEnabled = false;
            BtnExportTicket.IsEnabled = true;
            ShowMessage("Оплата прошла. Билет выписан — можно скачать с QR-кодом.", true);
        }
        catch (Exception ex) { ShowMessage(ex.Message, false); }
    }

    private void BtnExportTicket_Click(object? sender, RoutedEventArgs e)
    {
        if (!_orderId.HasValue || _currentEvent == null) return;

        try
        {
            var ticket = LoadTicketData(_orderId.Value);
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ArtPrestigeTickets");
            var (txtPath, pngPath, signature) = TicketExportService.Export(ticket, folder);
            ShowMessage($"Билет сохранён:\n{txtPath}\nQR: {pngPath}\nSHA256: {signature}", true);
        }
        catch (Exception ex) { ShowMessage($"Ошибка экспорта: {ex.Message}", false); }
    }

    // ---------- Таймер 15 минут ----------

    private void StartTimer()
    {
        _timeLeft = TimeSpan.FromMinutes(15);
        TxtTimer.Foreground = Brushes.Gold;
        TxtTimer.Text = FormatTimeLeft(_timeLeft);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timeLeft -= TimeSpan.FromSeconds(1);
        if (_timeLeft <= TimeSpan.Zero)
        {
            _timeLeft = TimeSpan.Zero;
            TxtTimer.Text = "00:00";
            StopTimer();
            OnTimerExpired();
            return;
        }

        // Последняя минута — красным, чтобы не пропустить.
        TxtTimer.Foreground = _timeLeft <= TimeSpan.FromMinutes(1) ? Brushes.OrangeRed : Brushes.Gold;
        TxtTimer.Text = FormatTimeLeft(_timeLeft);
    }

    private void OnTimerExpired()
    {
        if (_isPaid) return;
        _isExpired = true;

        if (_orderId.HasValue)
        {
            try { OrderService.ExpireBooking(_orderId.Value); } catch { /* уже закрыт */ }
        }

        TxtBookingStatus.Text = "Бронь истекла";
        TxtBookingStatus.Foreground = Brushes.Red;
        BtnPay.IsEnabled = false;
        ShowMessage("Время брони (15 мин) истекло. Заказ отменён, места освобождены.", false);
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= Timer_Tick;
            _timer = null;
        }
    }

    private static string FormatTimeLeft(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    // ---------- Загрузка данных заказа ----------

    private string LoadOrderReference(long orderId)
    {
        using var conn = DatabaseHelper.GetConnection();
        using var cmd = new MySqlCommand("SELECT order_reference FROM ticket_orders WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", orderId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
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

    private void ShowMessage(string message, bool isSuccess)
    {
        TxtBookingMessage.Text = message;
        TxtBookingMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtBookingMessage.IsVisible = true;
    }
}
