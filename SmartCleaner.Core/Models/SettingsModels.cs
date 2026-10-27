namespace SmartCleaner.Core.Models;

public sealed class ScanPathsConfig
{
    public List<string> Paths { get; set; } = [];
}

public sealed class CleaningSettingsConfig
{
    public bool UseRecycleBin { get; set; } = true;
    public string Theme { get; set; } = "System";
}

public sealed class UiSettingsConfig
{
    public string Theme { get; set; } = "System";

    /// <summary>Язык интерфейса: "Auto" | "ru" | "en" (ROADMAP v3.1, фаза L).</summary>
    public string Language { get; set; } = "Auto";
}

/// <summary>
/// Настройки сканирования (ROADMAP v3.1, День 9, фаза M): единая точка выбора
/// режима MFT для DiskUsage/LargeFiles/DiskMap — не хардкодится в сканеры.
/// </summary>
public sealed class ScanSettingsConfig
{
    /// <summary>
    /// Режим MFT-сканирования: "Auto" (NTFS + права → MFT, иначе фолбэк) |
    /// "On" (всегда попытка) | "Off" (никогда MFT, обход каталогов).
    /// Имя файла — <see cref="SmartCleaner.Core.Mft.MftDiskScanner.SettingsFilename"/>.
    /// </summary>
    public string MftScanMode { get; set; } = "Auto";
}
