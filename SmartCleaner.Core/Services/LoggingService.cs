using SmartCleaner.Core.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SmartCleaner.Core.Services;

/// <summary>
/// Сервис логирования сессий очистки
/// </summary>
public class LoggingService
{
    private readonly IConfigService _config;
    private readonly string _logDirectory;

    public LoggingService(IConfigService config)
    {
        _config = config;
        _logDirectory = config.LogDirectory;
        EnsureLogDirectory();
    }

    /// <summary>
    /// Записать результат сессии очистки
    /// </summary>
    public void LogCleaningSession(CleaningResult result, IEnumerable<ScannedItem> items)
    {
        var session = new CleaningSession
        {
            Timestamp = DateTime.Now,
            DeletedCount = result.DeletedCount,
            FreedBytes = result.FreedBytes,
            FailedCount = result.FailedCount,
            Duration = result.Duration,
            Items = items.Select(i => new CleanedItemLog
            {
                Path = i.Path,
                Size = i.Size,
                Category = i.ParentApp ?? "Unknown"
            }).ToList(),
            Errors = result.Errors.Select(e => new CleaningErrorLog
            {
                Path = e.Path,
                Message = e.Message
            }).ToList()
        };

        // Имя файла: cleaning_2024-01-15_143022.json
        var filename = $"cleaning_{DateTime.Now:yyyy-MM-dd_HHmmss}.json";
        var path = Path.Combine(_logDirectory, filename);

        try
        {
            var json = JsonSerializer.Serialize(session, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(path, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to write log: {ex.Message}");
        }
    }

    /// <summary>
    /// Получить последние N логов
    /// </summary>
    public List<CleaningSession> GetRecentLogs(int count = 10)
    {
        var logs = new List<CleaningSession>();

        if (!Directory.Exists(_logDirectory))
            return logs;

        var files = Directory.GetFiles(_logDirectory, "cleaning_*.json")
            .OrderByDescending(f => f)
            .Take(count);

        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var session = JsonSerializer.Deserialize<CleaningSession>(json);
                if (session != null)
                    logs.Add(session);
            }
            catch (Exception ex) { Debug.WriteLine($"[LoggingService] Log file read error: {ex.Message}"); }
        }

        return logs;
    }

    /// <summary>
    /// Получить общую статистику
    /// </summary>
    public CleaningStats GetTotalStats()
    {
        var logs = GetRecentLogs(100);
        
        return new CleaningStats
        {
            TotalSessions = logs.Count,
            TotalDeleted = logs.Sum(l => l.DeletedCount),
            TotalFreed = logs.Sum(l => l.FreedBytes),
            LastCleanup = logs.FirstOrDefault()?.Timestamp
        };
    }

    private void EnsureLogDirectory()
    {
        if (!Directory.Exists(_logDirectory))
        {
            try { Directory.CreateDirectory(_logDirectory); } catch { }
        }
    }
}

#region Log Models

public class CleaningSession
{
    public DateTime Timestamp { get; set; }
    public int DeletedCount { get; set; }
    public long FreedBytes { get; set; }
    public int FailedCount { get; set; }
    public TimeSpan Duration { get; set; }
    public List<CleanedItemLog> Items { get; set; } = new();
    public List<CleaningErrorLog> Errors { get; set; } = new();
}

public class CleanedItemLog
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Category { get; set; } = "";
}

public class CleaningErrorLog
{
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
}

public class CleaningStats
{
    public int TotalSessions { get; set; }
    public int TotalDeleted { get; set; }
    public long TotalFreed { get; set; }
    public DateTime? LastCleanup { get; set; }
}

#endregion
