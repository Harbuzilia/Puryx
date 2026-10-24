using SmartCleaner.Core.Localization;

namespace SmartCleaner.Core.Uninstaller;

public enum LeftoverType
{
    Folder,
    File,
    RegistryKey,
    RegistryValue,
    ScheduledTask,
    Service
}

public class LeftoverItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Path { get; set; } = string.Empty;
    public LeftoverType Type { get; set; }
    public string TypeName => Type switch
    {
        LeftoverType.Folder => Localizer.GetString("Leftover_Type_Folder"),
        LeftoverType.File => Localizer.GetString("Leftover_Type_File"),
        LeftoverType.RegistryKey => Localizer.GetString("Leftover_Type_RegistryKey"),
        LeftoverType.RegistryValue => Localizer.GetString("Leftover_Type_RegistryValue"),
        LeftoverType.ScheduledTask => Localizer.GetString("Leftover_Type_ScheduledTask"),
        LeftoverType.Service => Localizer.GetString("Leftover_Type_Service"),
        _ => Localizer.GetString("Leftover_Type_Unknown")
    };
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "-";
    public bool IsSelected { get; set; } = true;
}
