using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.IO;

namespace SmartCleaner.Core.Optimization;

public class SqliteDbTarget
{
    public string Name { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long OriginalSizeBytes { get; set; }
    public string OriginalSizeFormatted { get; set; } = "0 B";
    public long CompactedSizeBytes { get; set; }
    public string CompactedSizeFormatted { get; set; } = "0 B";
    public long SpaceSavedBytes { get; set; }
    public string Status { get; set; } = "Готов к сжатию";
    public bool IsSelected { get; set; } = true;
}

public class SqliteCompactorService
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    public async Task<List<SqliteDbTarget>> DiscoverDatabasesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var list = new List<SqliteDbTarget>();

        await Task.Run(() =>
        {
            var searchRoots = new (string AppName, string Path, string Pattern)[]
            {
                ("Visual Studio Code", Path.Combine(AppData, "Code", "User", "globalStorage"), "*.vscdb"),
                ("Cursor AI", Path.Combine(AppData, "Cursor", "User", "globalStorage"), "*.vscdb"),
                ("Windsurf", Path.Combine(UserProfile, ".codeium", "windsurf"), "*.db"),
                ("Google Chrome", Path.Combine(LocalAppData, "Google", "Chrome", "User Data", "Default"), "History"),
                ("Microsoft Edge", Path.Combine(LocalAppData, "Microsoft", "Edge", "User Data", "Default"), "History"),
                ("Telegram Desktop", Path.Combine(AppData, "Telegram Desktop", "tdata"), "*.db"),
                ("Antigravity", Path.Combine(UserProfile, ".gemini"), "*.db"),
                ("Claude Desktop", Path.Combine(AppData, "Claude"), "*.db")
            };

            foreach (var item in searchRoots)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(item.Path)) continue;

                try
                {
                    var files = item.Pattern.Contains('*')
                        ? Directory.EnumerateFiles(item.Path, item.Pattern, SearchOption.AllDirectories)
                        : File.Exists(Path.Combine(item.Path, item.Pattern)) ? new[] { Path.Combine(item.Path, item.Pattern) } : Array.Empty<string>();

                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Length > 200 * 1024) // > 200 KB
                            {
                                list.Add(new SqliteDbTarget
                                {
                                    Name = fi.Name,
                                    AppName = item.AppName,
                                    Path = file,
                                    OriginalSizeBytes = fi.Length,
                                    OriginalSizeFormatted = SizeFormatter.Format(fi.Length),
                                    IsSelected = true
                                });
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[SqliteCompactor] File info error: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[SqliteCompactor] Search root enumeration error: {ex.Message}"); }
            }
        }, ct);

        return list;
    }

    public async Task<(int CompactedCount, long SavedBytes)> CompactDatabasesAsync(IEnumerable<SqliteDbTarget> targets, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int count = 0;
        long totalSaved = 0;

        await Task.Run(() =>
        {
            foreach (var target in targets.Where(t => t.IsSelected))
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(target.Path)) continue;

                var initialSize = new FileInfo(target.Path).Length;
                progress?.Report($"Сжатие и вакуумизация базы: {target.Name} ({target.AppName})...");

                try
                {
                    var connString = $"Data Source={target.Path}";
                    using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connString);
                    conn.Open();

                    using var vacCmd = conn.CreateCommand();
                    vacCmd.CommandText = "VACUUM;";
                    vacCmd.ExecuteNonQuery();

                    using var checkpointCmd = conn.CreateCommand();
                    checkpointCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpointCmd.ExecuteNonQuery();

                    var newSize = new FileInfo(target.Path).Length;
                    var saved = Math.Max(0, initialSize - newSize);
                    target.CompactedSizeBytes = newSize;
                    target.CompactedSizeFormatted = SizeFormatter.Format(newSize);
                    target.SpaceSavedBytes = saved;
                    target.Status = saved > 0
                        ? $"Сжато (высвобождено {SizeFormatter.Format(saved)})"
                        : "Уже оптимизировано";

                    count++;
                    totalSaved += saved;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SqliteCompactor] Ошибка при сжатии {target.Path}: {ex.Message}");
                    target.Status = $"Ошибка: {ex.Message}";
                }
            }
        }, ct);

        return (count, totalSaved);
    }
}
