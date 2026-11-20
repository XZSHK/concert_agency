using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using MySql.Data.MySqlClient;
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
            TxtError.Text = "Пожалуйста, заполните все поля";
            TxtError.IsVisible = true;
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

            // 2. Покупатели (ticket_buyers.email + password).
            using (var cmd = new MySqlCommand(
                "SELECT full_name, password FROM ticket_buyers WHERE email = @login", connection))
            {
                cmd.Parameters.AddWithValue("@login", login);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    string fullName = reader.GetString("full_name");
                    string stored = reader.IsDBNull(reader.GetOrdinal("password"))
                        ? "" : reader.GetString("password");
                    reader.Close();

                    if (PasswordHasher.Verify(password, stored))
                    {
                        OpenDashboardForBuyer(fullName);
                        return;
                    }
                }
            }

            TxtError.Text = "Неверный логин или пароль";
            TxtError.Foreground = Brushes.Red;
            TxtError.IsVisible = true;
        }
        catch (Exception ex)
        {
            TxtError.Text = $"Ошибка БД: {ex.Message}";
            TxtError.Foreground = Brushes.Red;
            TxtError.IsVisible = true;
        }
    }

    private void OpenDashboardForRole(string roleName, string fullName)
    {
        Window dashboard;

        if (roleName.Contains("Продюсер"))
        {
            dashboard = new ProducerDashboardWindow(fullName);
        }
        else if (roleName.Contains("Менеджер"))
        {
            dashboard = new ManagerDashboardWindow(fullName);
        }
        else
        {
            dashboard = new DashboardWindow(fullName);
        }

        dashboard.Show();
        this.Close();
    }

    private void OpenDashboardForBuyer(string fullName)
    {
        var dashboard = new DashboardWindow(fullName);
        dashboard.Show();
        this.Close();
    }

    private void BtnGoToRegister_Click(object? sender, RoutedEventArgs e)
    {
        var registerWindow = new RegisterWindow();
        registerWindow.ShowDialog(this);
    }
}
