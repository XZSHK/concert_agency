using System.IO;
using QRCoder;

namespace Avalon.Services;

/// <summary>
/// Генерация QR-кода электронного билета (пакет QRCoder, PNG без System.Drawing).
/// </summary>
public static class QrCodeHelper
{
    /// <summary>Сохранить QR-код в PNG-файл. Возвращает путь к файлу.</summary>
    public static string SaveQrPng(string payload, string filePath, int pixelsPerModule = 20)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var qrCode = new PngByteQRCode(qrData);
        byte[] pngBytes = qrCode.GetGraphic(pixelsPerModule);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(filePath, pngBytes);
        return filePath;
    }

    /// <summary>Текстовая нагрузка QR-кода билета.</summary>
    public static string BuildPayload(
        string orderReference, string buyerName,
        string artistName, string eventDateText, string signature) =>
        $"PR23-KTZ|{orderReference}|{buyerName}|{artistName}|{eventDateText}|{signature}";
}
