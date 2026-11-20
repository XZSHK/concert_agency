using System;
using MySql.Data.MySqlClient;

namespace Avalon;

public static class DatabaseHelper
{
    // ВНИМАНИЕ: Замените "ваш_пароль" на реальный пароль от пользователя root в MySQL
    private static readonly string ConnectionString = "Server=localhost;Database=Concert_Agency;Uid=root;Pwd=";

    // Метод для получения открытого соединения
    public static MySqlConnection GetConnection()
    {
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}