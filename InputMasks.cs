using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Avalon;

/// <summary>
/// Входные маски, общие для всех окон.
/// Каждый форматер вызывается из TextChanged и принимает сырой текст —
/// возвращает отформатированную строку (каретка ставится в конец).
/// </summary>
public static class InputMasks
{
    // ---------- Телефон: +7 (999) 123-45-67 ----------
    public static string FormatPhone(string? input)
    {
        string digits = OnlyDigits(input);
        if (digits.Length == 0) return string.Empty;

        if (digits[0] == '8') digits = "7" + digits.Substring(1);
        else if (digits[0] != '7') digits = "7" + digits;
        if (digits.Length > 11) digits = digits.Substring(0, 11);

        var sb = new StringBuilder("+7");
        if (digits.Length > 1) sb.Append(" (").Append(digits.Substring(1, Math.Min(3, digits.Length - 1)));
        if (digits.Length > 4) sb.Append(") ").Append(digits.Substring(4, Math.Min(3, digits.Length - 4)));
        if (digits.Length > 7) sb.Append("-").Append(digits.Substring(7, Math.Min(2, digits.Length - 7)));
        if (digits.Length > 9) sb.Append("-").Append(digits.Substring(9, Math.Min(2, digits.Length - 9)));
        return sb.ToString();
    }

    public static bool IsCompletePhone(string? input)
    {
        string digits = OnlyDigits(input);
        return digits.Length == 11 && digits[0] == '7';
    }

    // ---------- Дата и время: дд.мм.гггг чч:мм ----------
    public static string FormatDateTime(string? input)
    {
        string d = OnlyDigits(input);
        if (d.Length > 12) d = d.Substring(0, 12);

        var sb = new StringBuilder();
        if (d.Length >= 1) sb.Append(d.Substring(0, Math.Min(2, d.Length)));
        if (d.Length >= 3) sb.Append(".").Append(d.Substring(2, Math.Min(2, d.Length - 2)));
        if (d.Length >= 5) sb.Append(".").Append(d.Substring(4, Math.Min(4, d.Length - 4)));
        if (d.Length >= 9) sb.Append(" ").Append(d.Substring(8, Math.Min(2, d.Length - 8)));
        if (d.Length >= 11) sb.Append(":").Append(d.Substring(10, Math.Min(2, d.Length - 10)));
        return sb.ToString();
    }

    public static bool TryParseEventDate(string? input, out DateTime value)
    {
        string s = (input ?? "").Trim();
        string[] formats = { "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yyyy", "yyyy-MM-dd HH:mm" };
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out value))
            return value > DateTime.MinValue;

        // Последний шанс — произвольная строка в текущей культуре.
        return DateTime.TryParse(s, CultureInfo.GetCultureInfo("ru-RU"),
            DateTimeStyles.None, out value);
    }

    // ---------- Дата (только день): дд.мм.гггг — фильтр менеджера ----------
    public static string FormatDate(string? input)
    {
        string d = OnlyDigits(input);
        if (d.Length > 8) d = d.Substring(0, 8);

        var sb = new StringBuilder();
        if (d.Length >= 1) sb.Append(d.Substring(0, Math.Min(2, d.Length)));
        if (d.Length >= 3) sb.Append(".").Append(d.Substring(2, Math.Min(2, d.Length - 2)));
        if (d.Length >= 5) sb.Append(".").Append(d.Substring(4, Math.Min(4, d.Length - 4)));
        return sb.ToString();
    }

    public static bool TryParseDate(string? input, out DateTime value) =>
        DateTime.TryParseExact((input ?? "").Trim(), new[] { "dd.MM.yyyy", "d.M.yyyy" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    // ---------- Деньги: только цифры и одна точка, максимум 2 знака ----------
    public static string FormatDecimal(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder();
        bool dot = false;
        int decimals = 0;

        foreach (char ch in input)
        {
            if (char.IsDigit(ch))
            {
                if (dot)
                {
                    if (decimals >= 2) continue;
                    decimals++;
                }
                if (!dot && sb.Length >= 9) continue; // не больше 9 знаков в целой части
                sb.Append(ch);
            }
            else if ((ch == '.' || ch == ',') && !dot)
            {
                dot = true;
                sb.Append('.');
            }
        }
        return sb.ToString();
    }

    public static bool TryParseDecimal(string? input, out decimal value)
    {
        string s = (input ?? "").Trim().Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && value >= 0;
    }

    // ---------- Возрастное ограничение: NN+ ----------
    public static string FormatAgeRestriction(string? input)
    {
        string d = OnlyDigits(input);
        if (d.Length > 2) d = d.Substring(0, 2);
        return d.Length == 0 ? string.Empty : d + "+";
    }

    // ---------- ФИО: только буквы, пробелы и дефис ----------
    private static readonly Regex NameInvalid = new(@"[^a-zA-Zа-яА-ЯёЁ\s\-]", RegexOptions.Compiled);
    public static string FormatFullName(string? input) => NameInvalid.Replace(input ?? "", string.Empty);

    public static bool IsValidFullName(string? input) =>
        Regex.IsMatch((input ?? "").Trim(), @"^[a-zA-Zа-яА-ЯёЁ]+([\-\s][a-zA-Zа-яА-ЯёЁ]+){1,2}$");

    public static bool IsValidEmail(string? input) =>
        Regex.IsMatch((input ?? "").Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$");

    private static string OnlyDigits(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        foreach (char ch in input) if (char.IsDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }
}
