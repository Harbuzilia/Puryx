namespace SmartCleaner.Core.Mft;

public class MftEntry
{
    public ulong FileReferenceNumber { get; set; }
    public ulong ParentFileReferenceNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public bool IsDirectory { get; set; }
    public DateTime LastWriteTime { get; set; }
}
