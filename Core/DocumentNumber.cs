using System;
using System.Text.RegularExpressions;

namespace Avalon.Core;

/// <summary>
/// Маска номера документа по ТЗ: PR23-KTZ-{Год}-{Номер:D5}
/// Пример: PR23-KTZ-2026-00042
/// </summary>
public static class DocumentNumber
{
    public const string Prefix = "PR23-KTZ";

    private static readonly Regex Pattern =
        new(@"^PR23-KTZ-(\d{4})-(\d{5})$", RegexOptions.Compiled);

    /// <summary>Собрать номер документа из года и порядкового номера.</summary>
    public static string Format(int year, int sequence)
    {
        if (sequence < 1 || sequence > 99999)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Номер должен быть от 1 до 99999 (формат D5).");

        return $"{Prefix}-{year}-{sequence:D5}";
    }

    /// <summary>Разобрать номер документа. Возвращает false, если маска не соблюдена.</summary>
    public static bool TryParse(string? value, out int year, out int sequence)
    {
        year = 0;
        sequence = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var m = Pattern.Match(value.Trim());
        if (!m.Success) return false;

        year = int.Parse(m.Groups[1].Value);
        sequence = int.Parse(m.Groups[2].Value);
        return true;
    }

    public static bool IsValid(string? value) => TryParse(value, out _, out _);

    /// <summary>Следующий порядковый номер для года по максимальному существующему.</summary>
    public static int NextSequence(int maxExistingSequence) => maxExistingSequence + 1;
}
