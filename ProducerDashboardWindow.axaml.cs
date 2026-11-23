using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
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

/// <summary>Строка списка «Концерты и афиши» — редактирование существующего концерта.</summary>
public class ProducerEventData
{
    public int Id { get; set; }
    public int HallId { get; set; }
    public string ArtistName { get; set; } = "";
    public string HallName { get; set; } = "";
    public DateTime EventDate { get; set; }
    public string AgeRestriction { get; set; } = "";
    public decimal ParterrePrice { get; set; }
    public decimal VipPrice { get; set; }
    public string ImagePath { get; set; } = "";
    public string ImageName => string.IsNullOrWhiteSpace(ImagePath) ? "—" : ImagePath;
}

public partial class ProducerDashboardWindow : Window
{
    public ObservableCollection<HallData> Halls { get; set; } = new();
    public ObservableCollection<FeeData> Fees { get; set; } = new();
    public ObservableCollection<ProducerEventData> Concerts { get; set; } = new();

    /// <summary>Id концерта, который редактируется; null — режим создания нового.</summary>
    private int? _editingEventId;

    private bool _isUpdatingDate = false;
    private bool _isUpdatingParterre = false;
    private bool _isUpdatingVip = false;
    private bool _isUpdatingAge = false;
    private bool _isUpdatingImage = false;

    public ProducerDashboardWindow()
    {
        InitializeComponent();
        this.DataContext = this;
        this.Opened += (s, e) => { LoadData(); LoadFees(); LoadConcerts(); UpdateImagePreview(TxtImagePath.Text); };
    }

    public ProducerDashboardWindow(string name) : this()
    {
        TxtUserName.Text = name;
    }

    // ---------- Загрузка данных ----------

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

    private void LoadConcerts()
    {
        try
        {
            const string withImage = @"
                SELECT ce.id, ce.hall_id, ce.artist_group_name, ce.event_date, ce.age_restriction,
                       ce.parterre_price, ce.vip_price, ce.image_path, ch.hall_name
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC";
            const string legacy = @"
                SELECT ce.id, ce.hall_id, ce.artist_group_name, ce.event_date, ce.age_restriction,
                       ce.parterre_price, ce.vip_price, ch.hall_name
                FROM concert_events ce
                JOIN concert_halls ch ON ce.hall_id = ch.id
                ORDER BY ce.event_date ASC";

            List<ProducerEventData> loaded;
            using var conn = DatabaseHelper.GetConnection();
            try
            {
                loaded = ReadConcerts(conn, withImage, includeImage: true);
            }
            catch (MySqlException)
            {
                // Столбец image_path отсутствует в старой БД — показываем список без афиш.
                loaded = ReadConcerts(conn, legacy, includeImage: false);
            }

            Concerts.Clear();
            foreach (var ev in loaded) Concerts.Add(ev);
        }
        catch (Exception ex) { Console.WriteLine(ex.Message); }
    }

    private static List<ProducerEventData> ReadConcerts(MySqlConnection conn, string query, bool includeImage)
    {
        var result = new List<ProducerEventData>();
        using var cmd = new MySqlCommand(query, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ProducerEventData
            {
                Id = reader.GetInt32("id"),
                HallId = reader.GetInt32("hall_id"),
                ArtistName = reader.GetString("artist_group_name"),
                HallName = reader.GetString("hall_name"),
                EventDate = reader.GetDateTime("event_date"),
                AgeRestriction = reader.IsDBNull(reader.GetOrdinal("age_restriction"))
                    ? "0+" : reader.GetString("age_restriction"),
                ParterrePrice = reader.GetDecimal("parterre_price"),
                VipPrice = reader.GetDecimal("vip_price"),
                ImagePath = includeImage && !reader.IsDBNull(reader.GetOrdinal("image_path"))
                    ? reader.GetString("image_path") : ""
            });
        }
        return result;
    }

    // ---------- Переключение панелей ----------

    private void ShowHalls_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = true; PanelConcerts.IsVisible = false; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = false; }

    private void ShowConcerts_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = false; PanelConcerts.IsVisible = true; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = false; LoadConcerts(); }

    private void ShowCreateEvent_Click(object? sender, RoutedEventArgs e)
    {
        PanelHalls.IsVisible = false; PanelConcerts.IsVisible = false; PanelCreateEvent.IsVisible = true; PanelFees.IsVisible = false;
        ResetEventForm();
    }

    private void ShowFees_Click(object? sender, RoutedEventArgs e)
    { PanelHalls.IsVisible = false; PanelConcerts.IsVisible = false; PanelCreateEvent.IsVisible = false; PanelFees.IsVisible = true; LoadFees(); }

    private void BtnRefreshFees_Click(object? sender, RoutedEventArgs e) => LoadFees();
    private void BtnRefreshHalls_Click(object? sender, RoutedEventArgs e) => LoadData();
    private void BtnRefreshConcerts_Click(object? sender, RoutedEventArgs e) => LoadConcerts();

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
        // null — показать placeholder из img/ + подсказку про перетаскивание.
        var bitmap = ImageSourceHelper.TryLoadBitmap(imagePath);
        ImgPreview.Source = bitmap ?? ImageSourceHelper.TryLoadBitmap(ImageSourceHelper.Placeholder);
        ImgDropHint.IsVisible = bitmap is null;
    }

    // ---------- Форма концерта (создание / редактирование) ----------

    private void ResetEventForm()
    {
        _editingEventId = null;
        TxtFormTitle.Text = "Новый концерт";
        BtnCreateEvent.Content = "СОЗДАТЬ КОНЦЕРТ";
        BtnCancelEdit.IsVisible = false;

        TxtArtistName.Text = "";
        TxtEventDate.Text = "";
        TxtParterrePrice.Text = "";
        TxtVipPrice.Text = "";
        TxtAgeRestriction.Text = "";
        TxtImagePath.Text = "";
        CmbHalls.SelectedItem = null;
        UpdateImagePreview(null);
    }

    private void BtnCancelEdit_Click(object? sender, RoutedEventArgs e) => ResetEventForm();

    private void BtnEditEvent_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not ProducerEventData ev) return;

        _editingEventId = ev.Id;

        TxtArtistName.Text = ev.ArtistName;
        CmbHalls.SelectedItem = Halls.FirstOrDefault(h => h.Id == ev.HallId);
        // TextChanged маски перестроят строку в тот же вид — это безопасно.
        TxtEventDate.Text = ev.EventDate.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        TxtParterrePrice.Text = ev.ParterrePrice.ToString("0.00", CultureInfo.InvariantCulture);
        TxtVipPrice.Text = ev.VipPrice.ToString("0.00", CultureInfo.InvariantCulture);
        TxtAgeRestriction.Text = ev.AgeRestriction;
        TxtImagePath.Text = ev.ImagePath;   // TextChanged обновит предпросмотр

        TxtFormTitle.Text = $"Редактирование: {ev.ArtistName}";
        BtnCreateEvent.Content = "СОХРАНИТЬ ИЗМЕНЕНИЯ";
        BtnCancelEdit.IsVisible = true;
        TxtMessage.IsVisible = false;

        PanelHalls.IsVisible = false; PanelConcerts.IsVisible = false;
        PanelFees.IsVisible = false; PanelCreateEvent.IsVisible = true;
    }

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

        // При редактировании прошедшего концерта задним числом — разрешаем любую дату.
        if (_editingEventId is null && parsedDate <= DateTime.Now)
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

            if (_editingEventId is int editingId)
            {
                SaveEvent(conn, editingId, selectedHall.Id, artist, parsedDate, age,
                    parterrePrice, vipPrice, imagePath);
                SetMessage("Концерт успешно обновлён!", true);
                ResetEventForm();
            }
            else
            {
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
            }

            LoadFees();
            LoadConcerts();
        }
        catch (Exception ex)
        {
            SetMessage($"Ошибка БД: {ex.Message}", false);
        }
    }

    private void SaveEvent(MySqlConnection conn, int id, int hallId, string artist, DateTime date,
        string age, decimal parterrePrice, decimal vipPrice, string imagePath)
    {
        const string withImage = @"UPDATE concert_events SET
            hall_id=@hallId, artist_group_name=@artist, event_date=@date, age_restriction=@age,
            parterre_price=@pPrice, vip_price=@vPrice, image_path=@imagePath
            WHERE id=@id";
        const string withoutImage = @"UPDATE concert_events SET
            hall_id=@hallId, artist_group_name=@artist, event_date=@date, age_restriction=@age,
            parterre_price=@pPrice, vip_price=@vPrice
            WHERE id=@id";

        try
        {
            ExecuteUpdate(conn, withImage, id, hallId, artist, date, age, parterrePrice, vipPrice, imagePath);
        }
        catch (MySqlException)
        {
            // Столбец image_path отсутствует в старой БД — обновляем остальное.
            ExecuteUpdate(conn, withoutImage, id, hallId, artist, date, age, parterrePrice, vipPrice, null);
        }
    }

    private static void ExecuteUpdate(MySqlConnection conn, string query,
        int id, int hallId, string artist, DateTime date, string age,
        decimal parterrePrice, decimal vipPrice, string? imagePath)
    {
        using var cmd = new MySqlCommand(query, conn);
        cmd.Parameters.AddWithValue("@id", id);
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

    // ---------- Перетаскивание файла афиши в зону изображения ----------

    private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private void ImgDropZone_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void ImgDropZone_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var file = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().FirstOrDefault();
        string? sourcePath = null;
        try { sourcePath = file?.TryGetLocalPath(); }
        catch { /* падение TryGetLocalPath — обработаем ниже */ }

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            SetMessage("Не удалось получить путь к файлу. Попробуйте выбрать файл на диске.", false);
            return;
        }

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(extension))
        {
            SetMessage("Неподдерживаемый формат. Используйте PNG, JPG, WEBP, GIF или BMP.", false);
            return;
        }

        string outputImgDir = Path.Combine(AppContext.BaseDirectory, "img");
        string? projectImgDir = FindProjectImgDir();

        // Имя файла в img/: безопасное, уникальное (кроме замены собственной афиши концерта).
        string safeName = SanitizeFileName(Path.GetFileName(sourcePath));
        string? currentImage = TxtImagePath.Text?.Trim();
        string destName = MakeUniqueImageName(safeName, currentImage, outputImgDir, projectImgDir);

        try
        {
            Directory.CreateDirectory(outputImgDir);
            File.Copy(sourcePath, Path.Combine(outputImgDir, destName), overwrite: true);

            if (projectImgDir != null)
            {
                Directory.CreateDirectory(projectImgDir);
                File.Copy(sourcePath, Path.Combine(projectImgDir, destName), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            SetMessage($"Не удалось скопировать файл в img/: {ex.Message}", false);
            return;
        }

        // Проверяем, что файл читается как изображение — иначе откатываем копии.
        if (ImageSourceHelper.TryLoadBitmap(destName) is null)
        {
            try { File.Delete(Path.Combine(outputImgDir, destName)); } catch { }
            if (projectImgDir != null) { try { File.Delete(Path.Combine(projectImgDir, destName)); } catch { } }
            SetMessage("Файл не удалось прочитать как изображение.", false);
            return;
        }

        TxtImagePath.Text = destName;   // обновит предпросмотр

        // Если редактируем существующий концерт — сразу сохраняем афишу в БД.
        if (_editingEventId is int editingId)
        {
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                const string withImage = @"UPDATE concert_events SET image_path=@imagePath WHERE id=@id";
                try
                {
                    using var cmd = new MySqlCommand(withImage, conn);
                    cmd.Parameters.AddWithValue("@imagePath", destName);
                    cmd.Parameters.AddWithValue("@id", editingId);
                    cmd.ExecuteNonQuery();
                    SetMessage($"Афиша «{destName}» скопирована в img/ и сохранена в БД.", true);
                }
                catch (MySqlException)
                {
                    SetMessage($"Афиша «{destName}» скопирована в img/, но в БД не сохранена: столбец image_path отсутствует (нажмите СОХРАНИТЬ — попытка повторится).", false);
                }
            }
            catch (Exception ex)
            {
                SetMessage($"Афиша скопирована в img/, но не сохранена в БД: {ex.Message}", false);
            }
            LoadConcerts();
        }
        else
        {
            SetMessage($"Афиша «{destName}» скопирована в img/. Не забудьте создать концерт — афиша сохранится в БД.", true);
        }
    }

    // Папка img/ исходников проекта: идём вверх от каталога сборки и ищем Avalon.csproj.
    private static string? FindProjectImgDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Avalon.csproj")))
                return Path.Combine(dir.FullName, "img");
        }
        return null;
    }

    private static string SanitizeFileName(string original)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var chars = original
            .Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch)
            .ToArray();
        string name = new string(chars).Trim().TrimStart('.');
        if (name.Length == 0) name = "poster" + Path.GetExtension(original);
        if (name.Length > 100)
        {
            string ext = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            stem = stem.Substring(0, Math.Max(1, 100 - ext.Length));
            name = stem + ext;
        }
        return name;
    }

    private static string MakeUniqueImageName(string safeName, string? currentImage,
        string outputImgDir, string? projectImgDir)
    {
        string currentFile = string.IsNullOrWhiteSpace(currentImage)
            ? "" : Path.GetFileName(currentImage.Trim());

        // Заменяем собственную афишу концерта — перезаписываем тот же файл.
        if (string.Equals(safeName, currentFile, StringComparison.OrdinalIgnoreCase))
            return safeName;

        bool ExistsAnywhere(string name) =>
            File.Exists(Path.Combine(outputImgDir, name)) ||
            (projectImgDir != null && File.Exists(Path.Combine(projectImgDir, name)));

        if (!ExistsAnywhere(safeName))
            return safeName;

        string stem = Path.GetFileNameWithoutExtension(safeName);
        string ext = Path.GetExtension(safeName);
        for (int i = 1; ; i++)
        {
            string candidate = $"{stem}_{i}{ext}";
            if (!ExistsAnywhere(candidate))
                return candidate;
        }
    }

    private void SetMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtMessage.IsVisible = true;
    }

    private void BtnLogout_Click(object? sender, RoutedEventArgs e) { new MainWindow().Show(); this.Close(); }
}
