using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using MySql.Data.MySqlClient;
using Avalon.Core;

namespace Avalon;

public partial class RegisterWindow : Window
{
    private bool _isUpdatingPhone = false;

    public RegisterWindow()
    {
        InitializeComponent();
    }

    // --- МАСКА ДЛЯ ТЕЛЕФОНА ---
    private void TxtPhone_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isUpdatingPhone) return;
        _isUpdatingPhone = true;

        string digits = new string(TxtPhone.Text?.Where(char.IsDigit).ToArray() ?? new char[0]);

        if (digits.Length > 11)
            digits = digits.Substring(0, 11);

        string formatted = "";

        if (digits.Length > 0)
        {
            if (digits[0] != '7')
            {
                digits = "7" + digits;
                if (digits.Length > 11) digits = digits.Substring(0, 11);
            }

            formatted = "+7";
            if (digits.Length > 1) formatted += " (" + digits.Substring(1, Math.Min(3, digits.Length - 1));
            if (digits.Length > 4) formatted += ") " + digits.Substring(4, Math.Min(3, digits.Length - 4));
            if (digits.Length > 7) formatted += "-" + digits.Substring(7, Math.Min(2, digits.Length - 7));
            if (digits.Length > 9) formatted += "-" + digits.Substring(9, Math.Min(2, digits.Length - 9));
        }

        TxtPhone.Text = formatted;
        TxtPhone.CaretIndex = formatted.Length;
        _isUpdatingPhone = false;
    }

    // --- БЛОКИРОВКА ЦИФР В ФИО ---
    private void TxtFullName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var currentText = TxtFullName.Text ?? "";
        var cleanedText = Regex.Replace(currentText, @"[^a-zA-Zа-яА-ЯёЁ\s\-]", "");

        if (currentText != cleanedText)
        {
            TxtFullName.Text = cleanedText;
            TxtFullName.CaretIndex = cleanedText.Length;
        }
    }

    private void BtnRegister_Click(object? sender, RoutedEventArgs e)
    {
        string fullName = TxtFullName.Text?.Trim() ?? "";
        string email = TxtEmail.Text?.Trim() ?? "";
        string phone = TxtPhone.Text?.Trim() ?? "";
        string password = TxtPassword.Text?.Trim() ?? "";
        bool hasPromo = ChkPromo.IsChecked ?? false;

        TxtMessage.IsVisible = false;

        // Валидация
        var nameParts = fullName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length < 2)
        {
            ShowMessage("Введите Фамилию и Имя (например: Иванов Иван)", false);
            return;
        }

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowMessage("Заполните обязательные поля (Email, Пароль)", false);
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowMessage("Введите корректный Email", false);
            return;
        }

        if (password.Length < 6)
        {
            ShowMessage("Пароль должен содержать минимум 6 символов", false);
            return;
        }

        // --- СОХРАНЕНИЕ В БД ---
        try
        {
            using (var connection = DatabaseHelper.GetConnection())
            {
                // 1. Проверка на существующий Email
                string checkQuery = "SELECT COUNT(*) FROM ticket_buyers WHERE email = @email";
                using (var checkCmd = new MySqlCommand(checkQuery, connection))
                {
                    checkCmd.Parameters.AddWithValue("@email", email);
                    long count = (long)checkCmd.ExecuteScalar();
                    if (count > 0)
                    {
                        ShowMessage("Пользователь с таким Email уже существует", false);
                        return;
                    }
                }

                // 2. Вставка нового пользователя
                string insertQuery = @"INSERT INTO ticket_buyers (full_name, email, phone, has_promo_subscription, password) 
                                       VALUES (@fullName, @email, @phone, @hasPromo, @password)";
                
                using (var cmd = new MySqlCommand(insertQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@fullName", fullName);
                    cmd.Parameters.AddWithValue("@email", email);
                    cmd.Parameters.AddWithValue("@phone", phone);
                    cmd.Parameters.AddWithValue("@hasPromo", hasPromo);
                    // Храним SHA256-хэш (совместимо с входом через PasswordHasher.Verify).
                    cmd.Parameters.AddWithValue("@password", PasswordHasher.Hash(password));
                    
                    cmd.ExecuteNonQuery();
                }
            }

            Console.WriteLine("[DB] Регистрация успешно сохранена!");

            // Переход на главную страницу
            var dashboard = new DashboardWindow(fullName);
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
            Console.WriteLine(ex.ToString());
        }
    }

    private void BtnBack_Click(object? sender, RoutedEventArgs e)
    {
        this.Close(); 
    }

    private bool IsValidEmail(string email)
    {
        var regex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        return regex.IsMatch(email);
    }

    private void ShowMessage(string message, bool isSuccess)
    {
        TxtMessage.Text = message;
        TxtMessage.Foreground = isSuccess ? Avalonia.Media.Brushes.Green : Avalonia.Media.Brushes.Red;
        TxtMessage.IsVisible = true;
    }
}