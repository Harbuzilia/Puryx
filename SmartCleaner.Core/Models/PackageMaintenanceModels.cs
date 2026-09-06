namespace SmartCleaner.Core.Models;

public enum CleaningActionTarget
{
    FileSystem,
    Package
}

public enum PackageManagerType
{
    Npm,
    Pip
}

public record PackageMaintenanceResult
{
    public int ProcessedCount { get; init; }
    public int SucceededCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public IReadOnlyList<CleaningError> Errors { get; init; } = [];
    public TimeSpan Duration { get; init; }
}

public record CleaningActionPartition
{
    public IReadOnlyList<ScannedItem> FileSystemItems { get; init; } = [];
    public IReadOnlyList<ScannedItem> PackageItems { get; init; } = [];
}
