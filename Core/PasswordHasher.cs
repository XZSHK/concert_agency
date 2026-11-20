using System;
using System.Security.Cryptography;
using System.Text;

namespace Avalon.Core;

/// <summary>
/// Хэширование паролей (SHA256 hex) для users.password_hash по ТЗ.
/// Verify принимает и старый открытый пароль (обратная совместимость
/// с уже существующей БД, где пароль хранился открытым текстом).
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
    {
        if (password == null) throw new ArgumentNullException(nameof(password));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Проверка пароля: сначала как хэш, затем (для старых записей) как открытый текст.
    /// </summary>
    public static bool Verify(string? input, string? stored)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(stored)) return false;

        string inputHash = Hash(input);
        if (string.Equals(inputHash, stored, StringComparison.OrdinalIgnoreCase))
            return true;

        // Обратная совместимость: в старой БД пароль лежал открытым текстом.
        return string.Equals(input, stored, StringComparison.Ordinal);
    }

    /// <summary>Нужно ли пересохранить пароль в виде хэша (миграция старых записей).</summary>
    public static bool NeedsRehash(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.Length != 64;
}
