namespace SmartCleaner.Core.Models;

/// <summary>
/// Режим удаления файлов
/// </summary>
public enum CleaningMode
{
    /// <summary>Отправить в корзину (безопаснее, можно восстановить)</summary>
    ToRecycleBin,
    
    /// <summary>Удалить навсегда (быстрее, но без отката)</summary>
    Permanent
}

/// <summary>
/// Результат очистки
/// </summary>
public record CleaningResult
{
    public int DeletedCount { get; init; }
    public long FreedBytes { get; init; }
    public int SkippedCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyList<CleaningError> Errors { get; init; } = [];
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Ошибка при очистке
/// </summary>
public record CleaningError
{
    public required string Path { get; init; }
    public required string Message { get; init; }
    public bool IsLocked { get; init; }
    public bool RequiresElevation { get; init; }
}

/// <summary>
/// Прогресс очистки
/// </summary>
public record CleaningProgress
{
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public long ProcessedBytes { get; init; }
    public long TotalBytes { get; init; }
    public string CurrentItem { get; init; } = string.Empty;
    public double Percentage => TotalCount > 0 ? (double)ProcessedCount / TotalCount * 100 : 0;
}

/// <summary>
/// Валидация перед удалением
/// </summary>
public record DeleteValidation
{
    public bool CanDelete { get; init; }
    public string? BlockReason { get; init; }
    public bool RequiresElevation { get; init; }
}
