using SmartCleaner.Core.Localization;

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
    public string Category { get; set; } = Localizer.GetString("Services_Cat_System");
    public ServiceRiskLevel RiskLevel { get; set; } = ServiceRiskLevel.SafeToDisable;
    public ServiceStartupType StartupType { get; set; } = ServiceStartupType.Unknown;
    public bool IsRunning { get; set; }

    public string StatusFormatted => IsRunning
        ? Localizer.GetString("Services_StatusRunning")
        : Localizer.GetString("Services_StatusStopped");
    public string StartupTypeFormatted => StartupType switch
    {
        ServiceStartupType.Automatic => Localizer.GetString("Services_StartupAuto"),
        ServiceStartupType.Manual => Localizer.GetString("Services_StartupManual"),
        ServiceStartupType.Disabled => Localizer.GetString("Services_StartupDisabled"),
        _ => Localizer.GetString("Services_StartupUnknown")
    };

    public string RiskFormatted => RiskLevel switch
    {
        ServiceRiskLevel.SafeToDisable => Localizer.GetString("Services_RiskSafe"),
        ServiceRiskLevel.Moderate => Localizer.GetString("Services_RiskModerate"),
        ServiceRiskLevel.Critical => Localizer.GetString("Services_RiskCritical"),
        _ => "?"
    };

    public bool IsDisabled => StartupType == ServiceStartupType.Disabled;
}
