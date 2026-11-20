using Avalonia.Controls;
using Avalonia.Interactivity;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Avalon;

public class HallData
{
    public int Id { get; set; }
    public string HallName { get; set; } = "";
    public string City { get; set; } = "";
    public int ParterreCapacity { get; set; }
    public int VipCapacity { get; set; }
    public decimal RentPerDay { get; set; }
}

public class FeeData
{
    public string ArtistName { get; set; } = "";
    public string HallName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public decimal TotalPotential { get; set; }
    public decimal Fee { get; set; }
}

public partial class ProducerDashboardWindow : Window
{
    public ObservableCollection<HallData> Halls { get; set; } = new();
    public ObservableCollection<FeeData> Fees { get; set; } = new();

    public ProducerDashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this; 
        this.Opened += (s, e) => { LoadData(); LoadFees(); };
    }

    public ProducerDashboardWindow(string name) : this()
    {
        TxtUserName.Text = name;
    }

    private void LoadData()
    {
        try
        {
            var tempList = new List<HallData>();
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = "SELECT id, hall_name, city, parterre_capacity, vip_capacity, base_rent_per_day FROM concert_halls";
                using (var cmd = new MySqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tempList.Add(new HallData {
                            Id = reader.GetInt32("id"),
                            HallName = reader.GetString("hall_name"),
                            City = reader.GetString("city"),
                            ParterreCapacity = reader.GetInt32("parterre_capacity"),
                            VipCapacity = reader.GetInt32("vip_capacity"),
                            RentPerDay = reader.GetDecimal("base_rent_per_day")
                        });
                    }
                }
            }
            Halls.Clear();
            foreach (var hall in tempList) Halls.Add(hall);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void LoadFees()
    {
        try
        {
            var tempList = new List<FeeData>();
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = @"
                    SELECT ce.artist_group_name, ce.event_date, ce.parterre_price, ce.vip_price,
                           ch.hall_name, ch.parterre_capacity, ch.vip_capacity
                    FROM concert_events ce
                    JOIN concert_halls ch ON ce.hall_id = ch.id
                    ORDER BY ce.event_date ASC";

                using (var cmd = new MySqlCommand(query, conn))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        decimal parterrePrice = reader.GetDecimal("parterre_price");
                        decimal vipPrice = reader.GetDecimal("vip_price");
                        int parterreCap = reader.GetInt32("parterre_capacity");
                        int vipCap = reader.GetInt32("vip_capacity");

                        decimal potential = (parterrePrice * parterreCap) + (vipPrice * vipCap);
                        decimal fee = potential * 0.30m; 

                        tempList.Add(new FeeData {
                            ArtistName = reader.GetString("artist_group_name"),
                            HallName = reader.GetString("hall_name"),
                            EventDate = reader.GetDateTime("event_date"),
                            TotalPotential = potential,
                            Fee = fee
                        });
                    }
                }
            }
            Fees.Clear();
            foreach (var f in tempList) Fees.Add(f);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void BtnRefreshFees_Click(object? sender, RoutedEventArgs e) => LoadFees();
    private void BtnRefreshHalls_Click(object? sender, RoutedEventArgs e) => LoadData();

    private void ShowHalls_Click(object? sender, RoutedEventArgs e) { PanelHalls.IsVisible = true; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = false; }
    private void ShowCreateEvent_Click(object? sender, RoutedEventArgs e) { PanelHalls.IsVisible = false; PanelCreateEvent.IsVisible = true; PanelFees.IsVisible = false; }
    private void ShowFees_Click(object? sender, RoutedEventArgs e) { PanelHalls.IsVisible = false; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = true; LoadFees(); }

    private void BtnCreateEvent_Click(object? sender, RoutedEventArgs e)
    {
        if (CmbHalls.SelectedItem is not HallData selectedHall)
        {
            TxtMessage.Text = "Выберите зал!"; TxtMessage.Foreground = Avalonia.Media.Brushes.Red; TxtMessage.IsVisible = true; return;
        }

        string dateInput = TxtEventDate.Text?.Trim() ?? "";
        if (!DateTime.TryParse(dateInput, out DateTime parsedDate))
        {
            TxtMessage.Text = "Неверный формат даты. Введите, например: 2026-12-31 20:00"; 
            TxtMessage.Foreground = Avalonia.Media.Brushes.Red; 
            TxtMessage.IsVisible = true; 
            return;
        }

        string formattedDate = parsedDate.ToString("yyyy-MM-dd HH:mm:ss");

        try
        {
            using (var conn = DatabaseHelper.GetConnection())
            {
                string query = @"INSERT INTO concert_events (hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price, image_path) 
                                 VALUES (@hallId, @artist, @date, @age, @pPrice, @vPrice, @imagePath)";
                using (var cmd = new MySqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@hallId", selectedHall.Id);
                    cmd.Parameters.AddWithValue("@artist", TxtArtistName.Text ?? "");
                    cmd.Parameters.AddWithValue("@date", formattedDate);
                    cmd.Parameters.AddWithValue("@age", TxtAgeRestriction.Text ?? "0+");
                    cmd.Parameters.AddWithValue("@pPrice", decimal.TryParse(TxtParterrePrice.Text, out var p) ? p : 0);
                    cmd.Parameters.AddWithValue("@vPrice", decimal.TryParse(TxtVipPrice.Text, out var v) ? v : 0);
                    cmd.Parameters.AddWithValue("@imagePath", TxtImagePath.Text ?? "");
                    cmd.ExecuteNonQuery();
                }
            }
            
            TxtMessage.Text = "Концерт успешно создан!"; 
            TxtMessage.Foreground = Avalonia.Media.Brushes.Green; 
            TxtMessage.IsVisible = true;
            
            TxtArtistName.Text = ""; TxtEventDate.Text = ""; TxtParterrePrice.Text = ""; TxtVipPrice.Text = ""; TxtAgeRestriction.Text = ""; TxtImagePath.Text = "";
            LoadFees();
        }
        catch (Exception ex) 
        { 
            TxtMessage.Text = $"Ошибка БД: {ex.Message}"; 
            TxtMessage.Foreground = Avalonia.Media.Brushes.Red; 
            TxtMessage.IsVisible = true; 
        }
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}