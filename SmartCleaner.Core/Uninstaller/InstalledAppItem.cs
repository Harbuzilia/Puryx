namespace SmartCleaner.Core.Uninstaller;

public class InstalledAppItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string DisplayVersion { get; set; } = string.Empty;
    public string InstallDate { get; set; } = string.Empty;
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public string QuietUninstallString { get; set; } = string.Empty;
    public long EstimatedSizeBytes { get; set; }
    public string EstimatedSizeFormatted { get; set; } = "0 B";
    public string RegistryKeyPath { get; set; } = string.Empty;
    public string AppType { get; set; } = "Win32"; // Win32, MSI, UWP, WinGet
    public string IconPath { get; set; } = string.Empty;
    public bool IsSystemComponent { get; set; }
}
