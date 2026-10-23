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
