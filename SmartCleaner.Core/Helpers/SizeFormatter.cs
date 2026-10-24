using SmartCleaner.Core.Localization;

namespace SmartCleaner.Core.Helpers;

/// <summary>
/// Утилита форматирования размеров файлов в читаемый вид
/// </summary>
public static class SizeFormatter
{
    // День 6б (фаза L): юниты размера — видимые строки интерфейса,
    // идут через Localizer (EN: B/KB/MB/GB/TB, RU: Б/КБ/МБ/ГБ/ТБ).
    // Утечку «0 Б» в EN-интерфейсе нашёл смоук дня 6б.
    private static readonly string[] UnitKeys =
        ["Size_Unit_Bytes", "Size_Unit_KB", "Size_Unit_MB", "Size_Unit_GB", "Size_Unit_TB"];

    /// <summary>
    /// Форматирует размер в байтах в читаемую строку (например, "1.5 ГБ")
    /// </summary>
    public static string Format(long bytes)
    {
        double len = bytes;
        int order = 0;

        while (len >= 1024 && order < UnitKeys.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {Localizer.GetString(UnitKeys[order])}";
    }
}
