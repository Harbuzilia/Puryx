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
        LeftoverType.Folder => "Папка",
        LeftoverType.File => "Файл",
        LeftoverType.RegistryKey => "Раздел реестра",
        LeftoverType.RegistryValue => "Параметр реестра",
        LeftoverType.ScheduledTask => "Задача планировщика",
        LeftoverType.Service => "Служба",
        _ => "Элемент"
    };
    public string Description { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "-";
    public bool IsSelected { get; set; } = true;
}
