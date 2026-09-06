namespace SmartCleaner.Core.CliInspector;

public sealed class PathHealthItem
{
    public string Path { get; set; } = string.Empty;
    public string Scope { get; set; } = "User"; // "User" or "System"
    public bool Exists { get; set; }
    public bool IsDead { get; set; }
    public string Status { get; set; } = string.Empty;
    public int BinCount { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public List<string> Executables { get; set; } = [];
}

public sealed class PowerShellProfileItem
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int LinesCount { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public string Content { get; set; } = string.Empty;
    public string LastModified { get; set; } = string.Empty;
    public bool IsModule { get; set; }
}

public sealed class CliInspectorScanResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int TotalToolsCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public string TotalSizeFormatted { get; set; } = "0 B";
    public int DeadPathsCount { get; set; }
    public int TotalPathsScanned { get; set; }
    public int PsProfilesCount { get; set; }
    public Dictionary<string, int> CategoriesCount { get; set; } = [];
    public List<CliToolItem> Tools { get; set; } = [];
    public List<PathHealthItem> PathHealth { get; set; } = [];
    public List<PowerShellProfileItem> PowerShellProfiles { get; set; } = [];
}
