using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartCleaner.Core.Duplicates;

/// <summary>
/// Запись об одном удалённом файле.
/// </summary>
public sealed class DeletedFileEntry
{
    public string OriginalPath { get; set; } = "";
    public long Size { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// Сессия удаления (одна операция «Удалить выбранные»).
/// </summary>
public sealed class DeletionSession
{
    public DateTime Timestamp { get; set; }
    public List<DeletedFileEntry> Files { get; set; } = [];
    public long TotalSize => Files.Sum(f => f.Size);
}

/// <summary>
/// Лог удалений в JSON-файл.
/// Хранится в %LOCALAPPDATA%/SmartCleaner/deletion_log.json.
/// Потокобезопасен через lock.
/// </summary>
public sealed class DeletionLogService
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartCleaner");

    private static readonly string LogPath = Path.Combine(LogDir, "deletion_log.json");
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _lock = new();

    /// <summary>
    /// Записывает сессию удаления в лог.
    /// Вызывать ПЕРЕД фактическим удалением файлов.
    /// </summary>
    /// <param name="files">Список файлов, которые будут удалены (путь + размер).</param>
    public void LogDeletion(IReadOnlyList<(string Path, long Size)> files)
    {
        if (files.Count == 0) return;

        var session = new DeletionSession
        {
            Timestamp = DateTime.Now,
            Files = files.Select(f => new DeletedFileEntry
            {
                OriginalPath = f.Path,
                Size = f.Size,
                DeletedAt = DateTime.Now
            }).ToList()
        };

        lock (_lock)
        {
            var history = LoadHistoryInternal();
            history.Add(session);

            // Ограничиваем — хранить не более 100 записей
            if (history.Count > 100)
                history.RemoveRange(0, history.Count - 100);

            SaveHistory(history);
        }
    }

    /// <summary>
    /// Загружает всю историю удалений.
    /// </summary>
    public List<DeletionSession> LoadHistory()
    {
        lock (_lock)
        {
            return LoadHistoryInternal();
        }
    }

    /// <summary>
    /// Очищает всю историю удалений.
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            if (File.Exists(LogPath))
                File.Delete(LogPath);
        }
    }

    private List<DeletionSession> LoadHistoryInternal()
    {
        try
        {
            if (!File.Exists(LogPath))
                return [];

            var json = File.ReadAllText(LogPath);
            return JsonSerializer.Deserialize<List<DeletionSession>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SaveHistory(List<DeletionSession> history)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            File.WriteAllText(LogPath, JsonSerializer.Serialize(history, JsonOpts));
        }
        catch { /* best-effort */ }
    }
}
