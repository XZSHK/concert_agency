using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using System.Linq;
using Avalon.Core;

namespace Avalon;

public partial class RegisterWindow : Window
{
    private bool _isUpdatingPhone = false;
    private bool _isUpdatingName = false;

    public RegisterWindow()
    {
        InitializeComponent();
    }

    // --- МАСКА ТЕЛЕФОНА: +7 (999) 123-45-67 ---
    private void TxtPhone_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPhone) return;
        _isUpdatingPhone = true;

        string formatted = InputMasks.FormatPhone(TxtPhone.Text);
        TxtPhone.Text = formatted;
        TxtPhone.CaretIndex = formatted.Length;

        _isUpdatingPhone = false;
    }

    // --- ФИО: только буквы, пробелы и дефис ---
    private void TxtFullName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingName) return;
        _isUpdatingName = true;

        string cleaned = InputMasks.FormatFullName(TxtFullName.Text);
        if (cleaned != TxtFullName.Text)
        {
            TxtFullName.Text = cleaned;
            TxtFullName.CaretIndex = cleaned.Length;
        }

        _isUpdatingName = false;
    }

    private void BtnRegister_Click(object? sender, RoutedEventArgs e)
    {
        string fullName = TxtFullName.Text?.Trim() ?? "";
        string email = TxtEmail.Text?.Trim() ?? "";
        string phone = TxtPhone.Text?.Trim() ?? "";
        string password = TxtPassword.Text?.Trim() ?? "";
        bool hasPromo = ChkPromo.IsChecked ?? false;

        TxtMessage.IsVisible = false;

        if (!InputMasks.IsValidFullName(fullName))
        {
            ShowMessage("Введите Фамилию и Имя (например: Иванов Иван)", false);
            return;
        }

        if (string.IsNullOrEmpty(email))
        {
            ShowMessage("Заполните Email", false);
            return;
        }

        if (!InputMasks.IsValidEmail(email))
        {
            ShowMessage("Введите корректный Email (например: example@mail.ru)", false);
            return;
        }

        if (!InputMasks.IsCompletePhone(phone))
        {
            ShowMessage("Введите телефон полностью: +7 (999) 123-45-67", false);
            return;
        }

        if (password.Length < 6)
        {
            ShowMessage("Пароль должен содержать минимум 6 символов", false);
            return;
        }

        try
        {
            using var connection = DatabaseHelper.GetConnection();

            // Проверка, что email ещё не занят
            using (var checkCmd = new MySqlCommand(
                "SELECT COUNT(*) FROM ticket_buyers WHERE email = @email", connection))
            {
                checkCmd.Parameters.AddWithValue("@email", email);
                if ((long)checkCmd.ExecuteScalar()! > 0)
                {
                    ShowMessage("Пользователь с таким Email уже существует", false);
                    return;
                }
            }

            // Сохраняем покупателя, запоминаем его id для кабинета
            long newBuyerId;
            using (var cmd = new MySqlCommand(
                @"INSERT INTO ticket_buyers (full_name, email, phone, has_promo_subscription, password)
                  VALUES (@fullName, @email, @phone, @hasPromo, @password)", connection))
            {
                cmd.Parameters.AddWithValue("@fullName", fullName);
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@phone", phone);
                cmd.Parameters.AddWithValue("@hasPromo", hasPromo);
                // SHA256-хэш пароля (до входа совместимо с PasswordHasher.Verify)
                cmd.Parameters.AddWithValue("@password", PasswordHasher.Hash(password));
                cmd.ExecuteNonQuery();
                newBuyerId = cmd.LastInsertedId;
            }

            var dashboard = new DashboardWindow((int)newBuyerId, fullName);
            dashboard.Show();

            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = dashboard;
                var loginWindow = desktop.Windows.FirstOrDefault(w => w is MainWindow);
                if (loginWindow != null) loginWindow.Close();
            }

            this.Close();
        }
        catch (Exception ex)
        {
            ShowMessage($"Ошибка БД: {ex.Message}", false);
        }
    }

    private void BtnBack_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void ShowMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Brushes.LightGreen : Brushes.Red;
        TxtMessage.IsVisible = true;
    }
}
