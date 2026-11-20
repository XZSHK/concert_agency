using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Avalon.Core;

/// <summary>
/// Цифровая подпись электронного билета (SHA256) по ТЗ:
/// конкатенация полей [Номер]+[Дата]+[Контрагент]+[Сумма]+[КоличествоПозиций].
/// </summary>
public static class TicketSignature
{
    /// <summary>Каноническая строка для подписи (единая для генерации и проверки).</summary>
    public static string BuildSource(
        string orderReference, DateTime orderDate,
        string counterparty, decimal totalAmount, int seatsCount)
    {
        string datePart = orderDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string sumPart = totalAmount.ToString("0.00", CultureInfo.InvariantCulture);
        return $"{orderReference}{datePart}{counterparty}{sumPart}{seatsCount}";
    }

    /// <summary>SHA256 (hex, lowercase) от конкатенации полей.</summary>
    public static string Compute(
        string orderReference, DateTime orderDate,
        string counterparty, decimal totalAmount, int seatsCount)
    {
        string source = BuildSource(orderReference, orderDate, counterparty, totalAmount, seatsCount);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Проверка подписи (защита от подделки билета).</summary>
    public static bool Verify(
        string orderReference, DateTime orderDate,
        string counterparty, decimal totalAmount, int seatsCount,
        string expectedSignature)
    {
        if (string.IsNullOrWhiteSpace(expectedSignature)) return false;
        string actual = Compute(orderReference, orderDate, counterparty, totalAmount, seatsCount);
        return string.Equals(actual, expectedSignature.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
