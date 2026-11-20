using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Linq;
using MySql.Data.MySqlClient;

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
            using (var connection = DatabaseHelper.GetConnection())
            {
                // 1. Ищем среди СОТРУДНИКОВ (таблица users + роли)
                string staffQuery = @"
                    SELECT u.full_name, r.name as role_name 
                    FROM users u
                    JOIN roles r ON u.role_id = r.id
                    WHERE u.login = @login AND u.password_hash = @password";

                using (var cmd = new MySqlCommand(staffQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@login", login);
                    cmd.Parameters.AddWithValue("@password", password);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string fullName = reader.GetString("full_name");
                            string roleName = reader.GetString("role_name");
                            
                            OpenDashboardForRole(roleName, fullName);
                            return;
                        }
                    }
                }

                // 2. Если не нашли среди сотрудников, ищем среди ПОКУПАТЕЛЕЙ
                string buyerQuery = "SELECT full_name FROM ticket_buyers WHERE email = @login AND password = @password";
                using (var cmd = new MySqlCommand(buyerQuery, connection))
                {
                    cmd.Parameters.AddWithValue("@login", login);
                    cmd.Parameters.AddWithValue("@password", password);
                    
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            string fullName = reader.GetString("full_name");
                            OpenDashboardForBuyer(fullName);
                            return;
                        }
                    }
                }
            }

            // Если не нашли нигде
            TxtError.Text = "Неверный логин или пароль";
            TxtError.Foreground = Avalonia.Media.Brushes.Red;
            TxtError.IsVisible = true;
        }
        catch (Exception ex)
        {
            TxtError.Text = $"Ошибка БД: {ex.Message}";
            TxtError.Foreground = Avalonia.Media.Brushes.Red;
            TxtError.IsVisible = true;
        }
    }

    // Метод для открытия нужного окна в зависимости от роли
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
            // На случай других ролей (например, Админ)
            dashboard = new DashboardWindow(fullName); 
        }

        dashboard.Show();
        this.Close();
    }

    private void OpenDashboardForBuyer(string fullName)
    {
        var dashboard = new DashboardWindow(fullName); // Обычный дашборд для покупателя
        dashboard.Show();
        this.Close();
    }

    private void BtnGoToRegister_Click(object? sender, RoutedEventArgs e)
    {
        var registerWindow = new RegisterWindow();
        registerWindow.ShowDialog(this); 
    }
}