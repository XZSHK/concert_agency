using Avalonia.Controls;
using Avalonia.Interactivity;
using MySql.Data.MySqlClient;
using System;

namespace Avalon;

public partial class BookingWindow : Window
{
    private ConcertEvent? _currentEvent;
    private string _userName = "";
    private int _parterreQty = 0;
    private int _vipQty = 0;
    private bool _hasPromoSubscription = false;

    public BookingWindow()
    {
        InitializeComponent();
    }

    public BookingWindow(ConcertEvent selectedEvent, string userName) : this()
    {
        _currentEvent = selectedEvent;
        _userName = userName;
        
        this.DataContext = _currentEvent;
        
        TxtConcertName.Text = _currentEvent.ArtistName;
        // Выводим дату и НАЗВАНИЕ ЗАЛА
        TxtConcertInfo.Text = $"{_currentEvent.EventDate:dd MMMM yyyy, HH:mm} | {_currentEvent.HallName}";
        
        CheckSubscription();
        UpdatePrices();
    }

    private void CheckSubscription()
    {
        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = "SELECT has_promo_subscription FROM ticket_buyers WHERE full_name = @name LIMIT 1";
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@name", _userName);
                    var result = cmd.ExecuteScalar();
                    if (result != null && Convert.ToBoolean(result))
                    {
                        _hasPromoSubscription = true;
                    }
                }
            }
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void UpdatePrices()
    {
        if (_currentEvent == null) return;

        decimal parterreTotal = _parterreQty * _currentEvent.ParterrePrice;
        decimal vipTotal = _vipQty * (_currentEvent.VipPrice + 500);
        decimal basePrice = parterreTotal + vipTotal;
        decimal serviceFee = _hasPromoSubscription ? 0 : basePrice * 0.10m;
        decimal total = basePrice + serviceFee;

        TxtParterrePrice.Text = $"{_currentEvent.ParterrePrice:C0} / шт";
        TxtVipPrice.Text = $"{_currentEvent.VipPrice + 500:C0} / шт";
        TxtBasePrice.Text = $"{basePrice:C0}";
        TxtServiceFee.Text = _hasPromoSubscription ? "0 руб. (Промокод)" : $"{serviceFee:C0}";
        TxtServiceFee.Foreground = _hasPromoSubscription ? Avalonia.Media.Brushes.Green : Avalonia.Media.Brushes.White;
        TxtTotal.Text = $"{total:C0}";
    }

    private void BtnPlusParterre_Click(object? sender, RoutedEventArgs e) { _parterreQty++; TxtParterreQty.Text = _parterreQty.ToString(); UpdatePrices(); }
    private void BtnMinusParterre_Click(object? sender, RoutedEventArgs e) { if (_parterreQty > 0) _parterreQty--; TxtParterreQty.Text = _parterreQty.ToString(); UpdatePrices(); }
    private void BtnPlusVip_Click(object? sender, RoutedEventArgs e) { _vipQty++; TxtVipQty.Text = _vipQty.ToString(); UpdatePrices(); }
    private void BtnMinusVip_Click(object? sender, RoutedEventArgs e) { if (_vipQty > 0) _vipQty--; TxtVipQty.Text = _vipQty.ToString(); UpdatePrices(); }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e) { this.Close(); }

    private void BtnConfirm_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentEvent == null) return;
        if (_parterreQty == 0 && _vipQty == 0) { Console.WriteLine("Выберите билеты!"); return; }

        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                int buyerId = 0;
                string findBuyer = "SELECT id FROM ticket_buyers WHERE full_name = @name LIMIT 1";
                using (var cmd = new MySqlCommand(findBuyer, conn))
                {
                    cmd.Parameters.AddWithValue("@name", _userName);
                    var res = cmd.ExecuteScalar();
                    if (res != null) buyerId = Convert.ToInt32(res);
                }
                if (buyerId == 0) return;

                decimal basePrice = (_parterreQty * _currentEvent.ParterrePrice) + (_vipQty * (_currentEvent.VipPrice + 500));
                decimal serviceFee = _hasPromoSubscription ? 0 : basePrice * 0.10m;
                decimal totalAmount = basePrice + serviceFee;
                string orderRef = $"PR23-КТЗ-{DateTime.Now.Year}-{new Random().Next(10000, 99999)}";

                long orderId = 0;
                string insertOrder = @"INSERT INTO ticket_orders (order_reference, user_id, buyer_id, event_id, order_datetime, service_fee, total_amount, status) 
                                       VALUES (@ref, 1, @buyer, @event, @date, @fee, @total, 'Бронь (15 мин)')";
                using (var cmd = new MySqlCommand(insertOrder, conn))
                {
                    cmd.Parameters.AddWithValue("@ref", orderRef);
                    cmd.Parameters.AddWithValue("@buyer", buyerId);
                    cmd.Parameters.AddWithValue("@event", _currentEvent.Id);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now);
                    cmd.Parameters.AddWithValue("@fee", serviceFee);
                    cmd.Parameters.AddWithValue("@total", totalAmount);
                    cmd.ExecuteNonQuery();
                    orderId = cmd.LastInsertedId;
                }

                for (int i = 0; i < _parterreQty; i++)
                {
                    string sql = "INSERT INTO ticket_order_seats (order_id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost) VALUES (@oid, 'Партер', 1, @seat, @base, @final)";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@oid", orderId); cmd.Parameters.AddWithValue("@seat", i + 1);
                        cmd.Parameters.AddWithValue("@base", _currentEvent.ParterrePrice); cmd.Parameters.AddWithValue("@final", _currentEvent.ParterrePrice);
                        cmd.ExecuteNonQuery();
                    }
                }
                for (int i = 0; i < _vipQty; i++)
                {
                    string sql = "INSERT INTO ticket_order_seats (order_id, sector_type, row_num, seat_num, base_seat_price, final_seat_cost) VALUES (@oid, 'VIP', 1, @seat, @base, @final)";
                    using (var cmd = new MySqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@oid", orderId); cmd.Parameters.AddWithValue("@seat", i + 1);
                        cmd.Parameters.AddWithValue("@base", _currentEvent.VipPrice); cmd.Parameters.AddWithValue("@final", _currentEvent.VipPrice + 500);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            this.Close();
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }
}