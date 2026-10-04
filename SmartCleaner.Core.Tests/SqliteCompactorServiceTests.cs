using Microsoft.Data.Sqlite;
using SmartCleaner.Core.Optimization;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 21 — L6 (ROADMAP): VACUUM только при незанятой БД.
/// Прежде CompactDatabasesAsync без проверки лез в базу, занятую живым
/// приложением-владельцем (браузер, мессенджер), и показывал пользователю
/// сырую «database is locked». Теперь: проба занятости перед VACUUM,
/// занятая база пропускается с честным статусом.
/// </summary>
public class SqliteCompactorServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); }
            catch (IOException) { /* файл ещё держится соединением-владельцем */ }
        }
    }

    private string CreateTempDbPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sqlite_compact_test_{Guid.NewGuid():N}.db");
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// База с «рыхлым» файлом: строки вставлены и удалены — страницы попадают
    /// в freelist, файл остаётся раздутым до VACUUM.
    /// </summary>
    private static void FillAndEmptyDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE payload(id INTEGER PRIMARY KEY, blob BLOB);";
            cmd.ExecuteNonQuery();
        }

        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO payload(blob) VALUES (@b);";
            var p = cmd.CreateParameter();
            p.ParameterName = "@b";
            cmd.Parameters.Add(p);
            var blob = new byte[2048];
            for (var i = 0; i < 2000; i++)
            {
                p.Value = blob;
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM payload;";
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task CompactDatabasesAsync_UnoccupiedDatabase_VacuumsAndReportsSavings()
    {
        var path = CreateTempDbPath();
        FillAndEmptyDatabase(path);
        var sizeBefore = new FileInfo(path).Length;
        var service = new SqliteCompactorService();
        var target = new SqliteDbTarget
        {
            Name = Path.GetFileName(path),
            AppName = "Test",
            Path = path,
            OriginalSizeBytes = sizeBefore,
            IsSelected = true
        };

        var (count, saved) = await service.CompactDatabasesAsync([target]);

        Assert.Equal(1, count);
        Assert.True(saved > 0, $"expected savings > 0, got {saved} (size before {sizeBefore})");
        Assert.StartsWith("Сжато", target.Status);
        Assert.True(target.CompactedSizeBytes < sizeBefore);
    }

    [Fact]
    public async Task CompactDatabasesAsync_DatabaseLockedByAnotherProcess_SkipsWithHonestStatus()
    {
        // Живое приложение-владелец держит write-lock (открытая транзакция записи)
        var path = CreateTempDbPath();
        FillAndEmptyDatabase(path);
        var service = new SqliteCompactorService();
        var target = new SqliteDbTarget
        {
            Name = Path.GetFileName(path),
            AppName = "Test",
            Path = path,
            OriginalSizeBytes = new FileInfo(path).Length,
            IsSelected = true
        };

        using var holder = new SqliteConnection($"Data Source={path}");
        holder.Open();
        using var holdTx = holder.BeginTransaction();
        using (var cmd = holder.CreateCommand())
        {
            cmd.Transaction = holdTx;
            cmd.CommandText = "CREATE TABLE hold_lock(x);";
            cmd.ExecuteNonQuery();
        }

        var (count, saved) = await service.CompactDatabasesAsync([target]);

        // Занятая база пропущена: не считается сжатой, экономия нулевая
        Assert.Equal(0, count);
        Assert.Equal(0, saved);
        Assert.StartsWith("Пропущено", target.Status);
        // Не сырой текст SQLite в UI
        Assert.DoesNotContain("database is locked", target.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ошибка", target.Status, StringComparison.OrdinalIgnoreCase);
    }
}
