using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalon.Core;
using Avalon.Services;

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

    private bool _isUpdatingDate = false;
    private bool _isUpdatingParterre = false;
    private bool _isUpdatingVip = false;
    private bool _isUpdatingAge = false;
    private bool _isUpdatingImage = false;

    public ProducerDashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this;
        this.Opened += (s, e) => { LoadData(); LoadFees(); UpdateImagePreview(TxtImagePath.Text); };
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
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(
                "SELECT id, hall_name, city, parterre_capacity, vip_capacity, base_rent_per_day FROM concert_halls", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tempList.Add(new HallData
                {
                    Id = reader.GetInt32("id"),
                    HallName = reader.GetString("hall_name"),
                    City = reader.GetString("city"),
                    ParterreCapacity = reader.GetInt32("parterre_capacity"),
                    VipCapacity = reader.GetInt32("vip_capacity"),
                    RentPerDay = reader.GetDecimal("base_rent_per_day")
                });
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
            using var conn = DatabaseHelper.GetConnection();
            using var cmd = new MySqlCommand(@"
                SELECT ce.artist_group_name, ce.event_date, ce.parterre_price, ce.vip_price,
                       ch.hall_name, ch.parterre_capacity, ch.vip_capacity
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                decimal parterrePrice = reader.GetDecimal("parterre_price");
                decimal vipPrice = reader.GetDecimal("vip_price");
                int parterreCap = reader.GetInt32("parterre_capacity");
                int vipCap = reader.GetInt32("vip_capacity");

                // Потенциал и гонорар (30%) — единая формула OrderCalculator.
                decimal potential = OrderCalculator.PotentialRevenue(parterrePrice, parterreCap, vipPrice, vipCap);
                decimal fee = OrderCalculator.CalculateProducerFee(potential);

                tempList.Add(new FeeData
                {
                    ArtistName = reader.GetString("artist_group_name"),
                    HallName = reader.GetString("hall_name"),
                    EventDate = reader.GetDateTime("event_date"),
                    TotalPotential = potential,
                    Fee = fee
                });
            }
            Fees.Clear();
            foreach (var f in tempList) Fees.Add(f);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private void BtnRefreshFees_Click(object? sender, RoutedEventArgs e) => LoadFees();
    private void BtnRefreshHalls_Click(object? sender, RoutedEventArgs e) => LoadData();

    private void ShowHalls_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = true; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = false; }

    private void ShowCreateEvent_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = false; PanelCreateEvent.IsVisible = true; PanelFees.IsVisible = false; }

    private void ShowFees_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = false; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = true; LoadFees(); }

    // ---------- Маски ввода ----------

    // Дата и время: дд.мм.гггг чч:мм
    private void TxtEventDate_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingDate) return;
        _isUpdatingDate = true;

        string formatted = InputMasks.FormatDateTime(TxtEventDate.Text);
        TxtEventDate.Text = formatted;
        TxtEventDate.CaretIndex = formatted.Length;

        _isUpdatingDate = false;
    }

    // Цены: только цифры и одна точка (не больше 2 знаков после запятой)
    private void TxtParterrePrice_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingParterre) return;
        _isUpdatingParterre = true;

        string formatted = InputMasks.FormatDecimal(TxtParterrePrice.Text);
        TxtParterrePrice.Text = formatted;
        TxtParterrePrice.CaretIndex = formatted.Length;

        _isUpdatingParterre = false;
    }

    private void TxtVipPrice_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingVip) return;
        _isUpdatingVip = true;

        string formatted = InputMasks.FormatDecimal(TxtVipPrice.Text);
        TxtVipPrice.Text = formatted;
        TxtVipPrice.CaretIndex = formatted.Length;

        _isUpdatingVip = false;
    }

    // Возраст: «12+» — цифры и автоматический плюс
    private void TxtAgeRestriction_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingAge) return;
        _isUpdatingAge = true;

        string formatted = InputMasks.FormatAgeRestriction(TxtAgeRestriction.Text);
        TxtAgeRestriction.Text = formatted;
        TxtAgeRestriction.CaretIndex = formatted.Length;

        _isUpdatingAge = false;
    }

    // Имя файла афиши → живой предпросмотр из папки img/
    private void TxtImagePath_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingImage) return;
        _isUpdatingImage = true;

        UpdateImagePreview(TxtImagePath.Text);

        _isUpdatingImage = false;
    }

    private void UpdateImagePreview(string? imagePath)
    {
        // null — показать placeholder из img/.
        ImgPreview.Source = ImageSourceHelper.TryLoadBitmap(imagePath)
            ?? ImageSourceHelper.TryLoadBitmap(ImageSourceHelper.Placeholder);
    }

    // ---------- Создание концерта ----------

    private void BtnCreateEvent_Click(object? sender, RoutedEventArgs e)
    {
        TxtMessage.IsVisible = false;

        if (CmbHalls.SelectedItem is not HallData selectedHall)
        {
            SetMessage("Выберите зал!", false);
            return;
        }

        string artist = TxtArtistName.Text?.Trim() ?? "";
        if (artist.Length < 2)
        {
            SetMessage("Укажите название группы (минимум 2 символа).", false);
            return;
        }

        if (!InputMasks.TryParseEventDate(TxtEventDate.Text, out DateTime parsedDate))
        {
            SetMessage("Неверная дата. Формат: дд.мм.гггг чч:мм (например: 31.12.2026 20:00)", false);
            return;
        }

        if (parsedDate <= DateTime.Now)
        {
            SetMessage("Дата концерта должна быть в будущем.", false);
            return;
        }

        if (!InputMasks.TryParseDecimal(TxtParterrePrice.Text, out decimal parterrePrice) || parterrePrice <= 0)
        {
            SetMessage("Укажите корректную цену партера (например: 1500.00).", false);
            return;
        }

        if (!InputMasks.TryParseDecimal(TxtVipPrice.Text, out decimal vipPrice) || vipPrice <= 0)
        {
            SetMessage("Укажите корректную цену VIP (например: 3000.00).", false);
            return;
        }

        string age = TxtAgeRestriction.Text?.Trim() ?? "";
        if (age.Length == 0) age = "0+";

        string imagePath = TxtImagePath.Text?.Trim() ?? "";

        try
        {
            using var conn = DatabaseHelper.GetConnection();

            // image_path есть не во всех существующих БД — пробуем с ним, иначе без него.
            const string withImage = @"INSERT INTO concert_events
                (hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price, image_path)
                VALUES (@hallId, @artist, @date, @age, @pPrice, @vPrice, @imagePath)";
            const string withoutImage = @"INSERT INTO concert_events
                (hall_id, artist_group_name, event_date, age_restriction, parterre_price, vip_price)
                VALUES (@hallId, @artist, @date, @age, @pPrice, @vPrice)";

            try
            {
                ExecuteCreate(conn, withImage, selectedHall.Id, artist, parsedDate, age,
                    parterrePrice, vipPrice, imagePath);
            }
            catch (MySqlException)
            {
                ExecuteCreate(conn, withoutImage, selectedHall.Id, artist, parsedDate, age,
                    parterrePrice, vipPrice, null);
            }

            SetMessage("Концерт успешно создан!", true);

            TxtArtistName.Text = "";
            TxtEventDate.Text = "";
            TxtParterrePrice.Text = "";
            TxtVipPrice.Text = "";
            TxtAgeRestriction.Text = "";
            TxtImagePath.Text = "";
            UpdateImagePreview(null);
            LoadFees();
        }
        catch (Exception ex)
        {
            SetMessage($"Ошибка БД: {ex.Message}", false);
        }
    }

    private static void ExecuteCreate(MySqlConnection conn, string query,
        int hallId, string artist, DateTime date, string age,
        decimal parterrePrice, decimal vipPrice, string? imagePath)
    {
        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@hallId", hallId);
        cmd.Parameters.AddWithValue("@artist", artist);
        cmd.Parameters.AddWithValue("@date", date);
        cmd.Parameters.AddWithValue("@age", age);
        cmd.Parameters.AddWithValue("@pPrice", parterrePrice);
        cmd.Parameters.AddWithValue("@vPrice", vipPrice);
        if (imagePath != null)
            cmd.Parameters.AddWithValue("@imagePath", imagePath);
        cmd.ExecuteNonQuery();
    }

    private void SetMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtMessage.IsVisible = true;
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}
