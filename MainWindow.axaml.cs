using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using MySql.Data.MySqlClient;
using System;
using Avalon.Core;

namespace Avalon;

public partial class MainWindow : Window
{
    // ТЗ: после трёх неверных попыток входа — блокировка на 10 секунд с обратным отсчётом.
    private int _failedAttempts;
    private DispatcherTimer? _lockoutTimer;
    private int _lockoutSecondsLeft;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnTogglePassword_Click(object? sender, RoutedEventArgs e)
    {
        // Переключение маски пароля: '*' ↔ пусто (показать текст).
        TxtPassword.PasswordChar = TxtPassword.PasswordChar == '*' ? '\0' : '*';
    }

    private void BtnLogin_Click(object? sender, RoutedEventArgs e)
    {
        if (_lockoutTimer != null)
        {
            // Блокировка активна — ввод недоступен (поля и кнопка отключены).
            return;
        }

        string login = TxtLogin.Text?.Trim() ?? "";
        string password = TxtPassword.Text?.Trim() ?? "";

        TxtError.IsVisible = false;

        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
        {
            ShowError("Пожалуйста, заполните все поля");
            return;
        }

        try
        {
            using var connection = DatabaseHelper.GetConnection();

            // 1. Сотрудники (users + roles). Пароль проверяем через PasswordHasher:
            //    принимает и SHA256-хэш (ТЗ: password_hash), и старый открытый пароль.
            using (var cmd = new MySqlCommand(
                @"SELECT u.full_name, u.password_hash, r.name AS role_name
                  FROM users u JOIN roles r ON u.role_id = r.id
                  WHERE u.login = @login", connection))
            {
                cmd.Parameters.AddWithValue("@login", login);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    string fullName = reader.GetString("full_name");
                    string storedHash = reader.IsDBNull(reader.GetOrdinal("password_hash"))
                        ? "" : reader.GetString("password_hash");
                    string roleName = reader.GetString("role_name");
                    reader.Close();

                    if (PasswordHasher.Verify(password, storedHash))
                    {
                        OpenDashboardForRole(roleName, fullName);
                        return;
                    }
                }
            }

            // 2. Покупатели (ticket_buyers.email + password). Запоминаем id — по нему грузятся заказы.
            using (var cmd = new MySqlCommand(
                "SELECT id, full_name, password FROM ticket_buyers WHERE email = @login", connection))
            {
                cmd.Parameters.AddWithValue("@login", login);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    int buyerId = reader.GetInt32("id");
                    string fullName = reader.GetString("full_name");
                    string stored = reader.IsDBNull(reader.GetOrdinal("password"))
                        ? "" : reader.GetString("password");
                    reader.Close();

                    if (PasswordHasher.Verify(password, stored))
                    {
                        OpenDashboardForBuyerId(buyerId, fullName);
                        return;
                    }
                }
            }

            ShowError("Неверный логин или пароль");
            RegisterFailedAttempt();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка БД: {ex.Message}");
        }
    }

    // Счётчик неверных попыток → блокировка на 10 секунд с обратным отсчётом.
    private void RegisterFailedAttempt()
    {
        _failedAttempts++;
        if (_failedAttempts < 3) return;

        _lockoutSecondsLeft = 10;
        TxtLogin.IsEnabled = false;
        TxtPassword.IsEnabled = false;
        BtnLogin.IsEnabled = false;
        BtnTogglePassword.IsEnabled = false;
        TxtLockout.IsVisible = true;
        UpdateLockoutText();

        _lockoutTimer?.Stop();
        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += LockoutTimer_Tick;
        _lockoutTimer.Start();
    }

    private void LockoutTimer_Tick(object? sender, EventArgs e)
    {
        _lockoutSecondsLeft--;
        if (_lockoutSecondsLeft <= 0)
        {
            StopLockout();
            return;
        }
        UpdateLockoutText();
    }

    private void UpdateLockoutText() =>
        TxtLockout.Text = $"Слишком много попыток входа. Повторите через {_lockoutSecondsLeft} с.";

    private void StopLockout()
    {
        _lockoutTimer?.Stop();
        _lockoutTimer = null;
        _failedAttempts = 0;
        TxtLogin.IsEnabled = true;
        TxtPassword.IsEnabled = true;
        BtnLogin.IsEnabled = true;
        BtnTogglePassword.IsEnabled = true;
        TxtLockout.IsVisible = false;
    }

    private void OpenDashboardForRole(string roleName, string fullName)
    {
        StopLockout(); // успешный вход: счётчик неверных попыток сброшен

        Window dashboard;

        if (roleName.Contains("Продюсер"))
            dashboard = new ProducerDashboardWindow(fullName);
        else if (roleName.Contains("Менеджер"))
            dashboard = new ManagerDashboardWindow(fullName);
        else
            // Прочие роли — кабинет покупателя без заказов (id = 0).
            dashboard = new DashboardWindow(0, fullName);

        dashboard.Show();
        this.Close();
    }

    private void OpenDashboardForBuyerId(int buyerId, string fullName)
    {
        StopLockout(); // успешный вход: счётчик неверных попыток сброшен

        var dashboard = new DashboardWindow(buyerId, fullName);
        dashboard.Show();
        this.Close();
    }

    // Вход по Enter в поле пароля.
    private void TxtPassword_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
            BtnLogin_Click(sender, e);
    }

    private void ShowError(string message)
    {
        TxtError.Text = message;
        TxtError.Foreground = Brushes.Red;
        TxtError.IsVisible = true;
    }

    private void BtnGoToRegister_Click(object? sender, RoutedEventArgs e)
    {
        var registerWindow = new RegisterWindow();
        registerWindow.ShowDialog(this);
    }
}
