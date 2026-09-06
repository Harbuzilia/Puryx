namespace SmartCleaner.Core.Helpers;

/// <summary>
/// Утилита форматирования размеров файлов в читаемый вид
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

    /// <summary>
    /// Форматирует размер в байтах в читаемую строку (например, "1.5 ГБ")
    /// </summary>
    public static string Format(long bytes)
    {
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < Units.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {Units[order]}";
    }
}
