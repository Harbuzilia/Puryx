namespace SmartCleaner.Core.CliInspector;

public sealed class CliToolItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public int FileCount { get; set; }
    public string LastModified { get; set; } = "Unknown";
    public DateTime? MTime { get; set; }
    public string UninstallCmd { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string ToolType { get; set; } = "folder"; // folder, package, executable, link, tool
    public string Version { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public List<string> Executables { get; set; } = [];
}
