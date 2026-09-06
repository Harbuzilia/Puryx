using SmartCleaner.Core.Stats;

namespace SmartCleaner.Core.Models;

/// <summary>
/// Статистика сессий очистки для дашборда «Сколько всего освобождено».
/// Лёгкая обёртка над CleaningStatsService (Stats namespace).
/// </summary>
public class CleaningStats
{
    /// <summary>Всего байт освобождено за всё время</summary>
    public long TotalFreedBytes { get; set; }

    /// <summary>Всего файлов удалено</summary>
    public long TotalDeletedCount { get; set; }

    /// <summary>Общее количество сессий очистки</summary>
    public int SessionCount { get; set; }

    /// <summary>Дата первой очистки</summary>
    public DateTime? FirstCleanDate { get; set; }

    /// <summary>Дата последней очистки</summary>
    public DateTime? LastCleanDate { get; set; }
}

/// <summary>
/// Обёртка для совместимости с MainViewModel.
/// Делегирует все операции единому CleaningStatsService из SmartCleaner.Core.Stats.
/// </summary>
public class CleaningStatsServiceLegacy
{
    private readonly CleaningStatsService _inner;

    public CleaningStatsServiceLegacy(CleaningStatsService inner)
    {
        _inner = inner;
    }

    /// <summary>
    /// Загрузить текущую статистику (совместимый формат).
    /// </summary>
    public CleaningStats Load()
    {
        var sessions = _inner.Sessions;
        return new CleaningStats
        {
            TotalFreedBytes = _inner.TotalFreedBytes,
            TotalDeletedCount = _inner.TotalFilesDeleted,
            SessionCount = _inner.TotalSessions,
            FirstCleanDate = sessions.Count > 0 ? sessions[^1].Date : null,
            LastCleanDate = sessions.Count > 0 ? sessions[0].Date : null
        };
    }

    /// <summary>
    /// Записать результат очистки в статистику.
    /// Делегирует в единый CleaningStatsService.
    /// </summary>
    public void RecordSession(long freedBytes, int deletedCount)
    {
        _inner.RecordSession(freedBytes, deletedCount);
    }
}
