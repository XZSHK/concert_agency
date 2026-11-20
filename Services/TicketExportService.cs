using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalon.Core;

namespace Avalon.Services;

/// <summary>
/// Экспорт электронного билета в файл (ТЗ: QR-код, схема прохода в сектор,
/// правила безопасности, цифровая подпись SHA256).
/// </summary>
public static class TicketExportService
{
    public sealed record TicketData(
        string OrderReference,
        DateTime OrderDate,
        string BuyerName,
        string ArtistName,
        string HallName,
        DateTime EventDate,
        string AgeRestriction,
        decimal TotalAmount,
        IReadOnlyList<SeatLine> Seats);

    public sealed record SeatLine(string SectorType, int RowNum, int SeatNum, decimal FinalCost);

    /// <summary>Собрать текст электронного билета (включая подпись SHA256).</summary>
    public static string BuildTicketText(TicketData ticket)
    {
        string signature = TicketSignature.Compute(
            ticket.OrderReference, ticket.OrderDate,
            ticket.BuyerName, ticket.TotalAmount, ticket.Seats.Count);

        var sb = new StringBuilder();
        sb.AppendLine("============================================================");
        sb.AppendLine("  ЭЛЕКТРОННЫЙ БИЛЕТ — Концертно-гастрольное агентство");
        sb.AppendLine("  «Арт-Престиж» (PR23-KTZ)");
        sb.AppendLine("============================================================");
        sb.AppendLine($"Номер документа : {ticket.OrderReference}");
        sb.AppendLine($"Дата оформления : {ticket.OrderDate:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Покупатель      : {ticket.BuyerName}");
        sb.AppendLine($"Концерт         : {ticket.ArtistName}");
        sb.AppendLine($"Площадка        : {ticket.HallName}");
        sb.AppendLine($"Дата и время    : {ticket.EventDate:dd MMMM yyyy, HH:mm} (возраст {ticket.AgeRestriction})");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine("Места:");
        foreach (var seat in ticket.Seats)
            sb.AppendLine($"  - {seat.SectorType}, ряд {seat.RowNum}, место {seat.SeatNum} — {seat.FinalCost:0.00} руб.");
        sb.AppendLine("------------------------------------------------------------");
        sb.AppendLine($"ИТОГО к оплате  : {ticket.TotalAmount:0.00} руб.");
        sb.AppendLine();
        sb.AppendLine("Схема прохода в сектор:");
        sb.AppendLine("  1. На входе предъявите QR-код билета контролёру.");
        sb.AppendLine("  2. Партер: вход через центральный вестибюль, далее по указателям.");
        sb.AppendLine("  3. VIP: отдельный вход с бейджем, гардероб VIP, выход на террасу.");
        sb.AppendLine("  4. После контроля на входе билет считается использованным.");
        sb.AppendLine();
        sb.AppendLine("Правила безопасности:");
        sb.AppendLine("  - Запрещён пронос запрещённых предметов; действует досмотр.");
        sb.AppendLine("  - Возврат билетов — не позднее чем за 24 часа до концерта.");
        sb.AppendLine("  - Бронь удерживается 15 минут до завершения онлайн-оплаты.");
        sb.AppendLine();
        sb.AppendLine($"QR-код содержит: {QrCodeHelper.BuildPayload(ticket.OrderReference, ticket.BuyerName, ticket.ArtistName, ticket.EventDate.ToString("yyyy-MM-dd HH:mm"), signature)}");
        sb.AppendLine($"Цифровая подпись SHA256: {signature}");
        sb.AppendLine("============================================================");
        return sb.ToString();
    }

    /// <summary>
    /// Сохранить билет (.txt) и QR-код (.png) в папку. Возвращает (путь к txt, путь к png, подпись).
    /// </summary>
    public static (string TxtPath, string PngPath, string Signature) Export(
        TicketData ticket, string folder)
    {
        Directory.CreateDirectory(folder);

        string signature = TicketSignature.Compute(
            ticket.OrderReference, ticket.OrderDate,
            ticket.BuyerName, ticket.TotalAmount, ticket.Seats.Count);

        string safeRef = ticket.OrderReference.Replace("/", "_");
        string txtPath = Path.Combine(folder, $"ticket_{safeRef}.txt");
        string pngPath = Path.Combine(folder, $"ticket_{safeRef}_qr.png");

        File.WriteAllText(txtPath, BuildTicketText(ticket), Encoding.UTF8);

        string payload = QrCodeHelper.BuildPayload(
            ticket.OrderReference, ticket.BuyerName,
            ticket.ArtistName, ticket.EventDate.ToString("yyyy-MM-dd HH:mm"), signature);
        QrCodeHelper.SaveQrPng(payload, pngPath);

        return (txtPath, pngPath, signature);
    }
}
