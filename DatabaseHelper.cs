using System;
using MySql.Data.MySqlClient;

namespace Avalon;

/// <summary>
/// Подключение к MySQL (база Concert_Agency, 7 таблиц по ТЗ).
/// Строку подключения можно переопределить переменной окружения
/// CONCERT_DB_CONNECTION, например:
///   Server=localhost;Database=Concert_Agency;Uid=root;Pwd=ваш_пароль
/// По умолчанию — локальный root без пароля (как в исходном проекте).
/// </summary>
public static class DatabaseHelper
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CONCERT_DB_CONNECTION")
        ?? "Server=localhost;Database=Concert_Agency;Uid=root;Pwd=";

    public static string GetConnectionString() => ConnectionString;

    /// <summary>Получить открытое соединение с БД.</summary>
    public static MySqlConnection GetConnection()
    {
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }
}
