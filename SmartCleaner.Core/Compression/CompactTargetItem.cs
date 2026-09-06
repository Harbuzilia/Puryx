namespace SmartCleaner.Core.Compression;

public class CompactTargetItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Category { get; set; } = "Games"; // Games, System, Developer, Custom
    public long OriginalSizeBytes { get; set; }
    public string OriginalSizeFormatted { get; set; } = "0 B";
    public long CompressedSizeBytes { get; set; }
    public string CompressedSizeFormatted { get; set; } = "0 B";
    public long SpaceSavedBytes { get; set; }
    public string SpaceSavedFormatted { get; set; } = "0 B";
    public double CompressionPercent { get; set; }
    public bool IsCompressed { get; set; }
    public string Status { get; set; } = "Не сжато";
    public string RecommendedAlgorithm { get; set; } = "LZX"; // LZX, XPRESS16K
}
