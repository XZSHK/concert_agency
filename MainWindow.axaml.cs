using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using MySql.Data.MySqlClient;
using System;
using Avalon.Core;

namespace Avalon;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnLogin_Click(object? sender, RoutedEventArgs e)
    {
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
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка БД: {ex.Message}");
        }
    }

    private void OpenDashboardForRole(string roleName, string fullName)
    {
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
