namespace SmartCleaner.Core.ServicesOpt;

public enum ServiceStartupType
{
    Automatic = 2,
    Manual = 3,
    Disabled = 4,
    Unknown = 0
}

public enum ServiceRiskLevel
{
    SafeToDisable,
    Moderate,
    Critical
}

public enum ServiceProfileType
{
    Gaming,
    Balanced,
    Workstation,
    RestoreDefaults
}

public class WindowsServiceItem
{
    public string ServiceName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Система";
    public ServiceRiskLevel RiskLevel { get; set; } = ServiceRiskLevel.SafeToDisable;
    public ServiceStartupType StartupType { get; set; } = ServiceStartupType.Unknown;
    public bool IsRunning { get; set; }

    public string StatusFormatted => IsRunning ? "🟢 Работает" : "⚪ Остановлена";
    public string StartupTypeFormatted => StartupType switch
    {
        ServiceStartupType.Automatic => "Авто",
        ServiceStartupType.Manual => "Вручную",
        ServiceStartupType.Disabled => "Отключена",
        _ => "Неизвестно"
    };

    public string RiskFormatted => RiskLevel switch
    {
        ServiceRiskLevel.SafeToDisable => "Безопасно",
        ServiceRiskLevel.Moderate => "Внимание",
        ServiceRiskLevel.Critical => "Критическая",
        _ => "?"
    };

    public bool IsDisabled => StartupType == ServiceStartupType.Disabled;
}
