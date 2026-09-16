using System.Diagnostics;
using System.Text.Json;

namespace SmartCleaner.Core.Stats;

/// <summary>
/// Запись статистики одной сессии очистки.
/// </summary>
public sealed class CleaningSession
{
    public DateTime Date { get; set; }
    public long FreedBytes { get; set; }
    public int FilesDeleted { get; set; }
    public string Profile { get; set; } = "";
}

/// <summary>
/// Сервис хранения и агрегации статистики очисток.
/// Данные в JSON: %LOCALAPPDATA%/SmartCleaner/cleaning_stats.json.
/// </summary>
public sealed class CleaningStatsService
{
    private readonly string _filePath;
    private List<CleaningSession> _sessions = [];

    public CleaningStatsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartCleaner"))
    {
    }

    internal CleaningStatsService(string statsDirectory)
    {
        try
        {
            Directory.CreateDirectory(statsDirectory);
        }
        catch
        {
            // Если не удалось создать директорию — работаем без сохранения статистики
        }
        _filePath = Path.Combine(statsDirectory, "cleaning_stats.json");
        Load();
    }

    /// <summary>Список всех сессий очистки (новые сверху).</summary>
    public IReadOnlyList<CleaningSession> Sessions => _sessions;

    /// <summary>Общее количество сессий.</summary>
    public int TotalSessions => _sessions.Count;

    /// <summary>Общее количество удалённых байтов за всё время.</summary>
    public long TotalFreedBytes => _sessions.Sum(s => s.FreedBytes);

    /// <summary>Общее количество удалённых файлов за всё время.</summary>
    public int TotalFilesDeleted => _sessions.Sum(s => s.FilesDeleted);

    /// <summary>
    /// Записывает новую сессию очистки.
    /// </summary>
    public void RecordSession(long freedBytes, int filesDeleted, string profile = "")
    {
        _sessions.Insert(0, new CleaningSession
        {
            Date = DateTime.Now,
            FreedBytes = freedBytes,
            FilesDeleted = filesDeleted,
            Profile = profile
        });

        // Храним максимум 365 записей
        if (_sessions.Count > 365)
            _sessions = _sessions.Take(365).ToList();

        Save();
    }

    /// <summary>
    /// Агрегация по месяцам (для графика): последние N месяцев.
    /// </summary>
    public List<(string Label, long TotalBytes, int TotalFiles)> GetMonthlyStats(int months = 12)
    {
        var result = new List<(string, long, int)>();
        var now = DateTime.Now;

        for (int i = months - 1; i >= 0; i--)
        {
            var targetMonth = now.AddMonths(-i);
            var label = targetMonth.ToString("MMM yy");
            var sessionsInMonth = _sessions.Where(s =>
                s.Date.Year == targetMonth.Year && s.Date.Month == targetMonth.Month);

            result.Add((label, sessionsInMonth.Sum(s => s.FreedBytes), sessionsInMonth.Sum(s => s.FilesDeleted)));
        }

        return result;
    }

    /// <summary>
    /// Агрегация по неделям (для графика): последние N недель.
    /// </summary>
    public List<(string Label, long TotalBytes, int TotalFiles)> GetWeeklyStats(int weeks = 12)
    {
        var result = new List<(string, long, int)>();
        var now = DateTime.Now.Date;

        for (int i = weeks - 1; i >= 0; i--)
        {
            var weekStart = now.AddDays(-7 * i - (int)now.DayOfWeek + 1);
            var weekEnd = weekStart.AddDays(7);
            var label = weekStart.ToString("dd.MM");
            var sessionsInWeek = _sessions.Where(s => s.Date >= weekStart && s.Date < weekEnd);

            result.Add((label, sessionsInWeek.Sum(s => s.FreedBytes), sessionsInWeek.Sum(s => s.FilesDeleted)));
        }

        return result;
    }

    /// <summary>
    /// Очищает всю статистику.
    /// </summary>
    public void Clear()
    {
        _sessions.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _sessions = JsonSerializer.Deserialize<List<CleaningSession>>(json) ?? [];
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Stats] Load error: {ex.Message}"); _sessions = []; }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_sessions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) { Debug.WriteLine($"[Stats] Save error: {ex.Message}"); }
    }
}
