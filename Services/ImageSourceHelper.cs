using System;
using System.IO;

namespace Avalon.Services;

/// <summary>
/// Разрешает путь к изображению афиши:
///  1) абсолютный URL (http/https) или file:// — как есть;
///  2) абсолютный путь на диске — как file://;
///  3) имя файла — ищем в папке img/ рядом с приложением (она копируется в вывод сборки),
///     затем в img/ исходников (рабочий каталог при запуске из VS);
///  4) иначе — встроенный avares://Avalon/img/{файл};
///  5) последняя надежда — placeholder.
/// </summary>
public static class ImageSourceHelper
{
    public const string Placeholder = "avares://Avalon/img/placeholder.jpg";

    public static string Resolve(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return Placeholder;

        string path = imagePath.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "file"))
            return path;

        if (Path.IsPathRooted(path) && File.Exists(path))
            return new Uri(path).AbsoluteUri;

        // Папка img/ рядом с исполняемым файлом (копируется при сборке).
        string baseDir = AppContext.BaseDirectory;
        string candidate = Path.GetFullPath(Path.Combine(baseDir, "img", path));
        if (File.Exists(candidate))
            return new Uri(candidate).AbsoluteUri;

        // Запуск из VS с рабочим каталогом в корне проекта.
        string projectRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
        candidate = Path.GetFullPath(Path.Combine(projectRoot, "img", path));
        if (File.Exists(candidate))
            return new Uri(candidate).AbsoluteUri;

        // Встроенный ресурс (файлы, которые были в img/ на момент сборки).
        return $"avares://Avalon/img/{path}";
    }

    /// <summary>
    /// Загружает изображение для предпросмотра (file:// или встроенный avares:// ресурс).
    /// Возвращает null, если файл не найден/не читается — вызывающий код решает, что показать.
    /// </summary>
    public static Avalonia.Media.Imaging.Bitmap? TryLoadBitmap(string? imagePath)
    {
        try
        {
            string resolved = Resolve(imagePath);
            var uri = new Uri(resolved);

            if (uri.Scheme == "avares")
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(uri);
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }

            string filePath = uri.IsFile ? uri.LocalPath : resolved;
            return File.Exists(filePath) ? new Avalonia.Media.Imaging.Bitmap(filePath) : null;
        }
        catch
        {
            return null;
        }
    }
}
