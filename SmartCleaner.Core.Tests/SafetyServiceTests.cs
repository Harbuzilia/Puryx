using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class SafetyServiceTests
{
    private static SafetyService CreateService()
    {
        var config = new InMemoryConfigService([]);
        return new SafetyService(config, new KnowledgeBase(config));
    }

    private static ScannedItem MakeItem(string path, RiskCategory risk = RiskCategory.PerformanceCache, bool isDirectory = false)
    {
        return new ScannedItem
        {
            Path = path,
            Size = 100,
            Risk = risk,
            Description = "test",
            IsDirectory = isDirectory
        };
    }

    // ─── Whitelist ────────────────────────────────

    [Fact]
    public void IsWhitelisted_GitFolder_MatchesBuiltInPattern()
    {
        var service = CreateService();

        Assert.True(service.IsWhitelisted(@"C:\repos\myproject\.git\config"));
        Assert.True(service.IsWhitelisted(@"D:\work\.git\objects\pack\a.pack"));
    }

    [Fact]
    public void IsWhitelisted_GitDirectoryItself_IsProtectedWhenExists()
    {
        // Регрессия: паттерн «**\.git\**» раньше не защищал саму папку .git —
        // её удаление уничтожало бы весь репозиторий
        var service = CreateService();
        var root = Path.Combine(Path.GetTempPath(), $"gitdir-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(root, ".git");
        Directory.CreateDirectory(gitDir);
        try
        {
            Assert.True(service.IsWhitelisted(gitDir));

            var validation = service.ValidateForDeletion(MakeItem(gitDir, isDirectory: true));
            Assert.False(validation.CanDelete);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsWhitelisted_ExpandedUserProfilePattern_Matches()
    {
        var service = CreateService();
        var geminiPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "state.json");

        Assert.True(service.IsWhitelisted(geminiPath));
    }

    [Fact]
    public void IsWhitelisted_RegularCachePath_DoesNotMatch()
    {
        var service = CreateService();

        Assert.False(service.IsWhitelisted(@"C:\Users\Test\AppData\Local\npm-cache\_cacache"));
    }

    [Fact]
    public void Whitelist_UserPattern_AddRemoveRoundTrip()
    {
        var service = CreateService();
        const string pattern = @"C:\Users\Test\ImportantStuff\**";

        service.AddToWhitelist(pattern);
        Assert.Contains(pattern, service.GetWhitelistPatterns());
        Assert.True(service.IsWhitelisted(@"C:\Users\Test\ImportantStuff\file.txt"));

        service.RemoveFromWhitelist(pattern);
        Assert.DoesNotContain(pattern, service.GetWhitelistPatterns());
        Assert.False(service.IsWhitelisted(@"C:\Users\Test\ImportantStuff\file.txt"));
    }

    [Fact]
    public void Whitelist_DuplicateUserPattern_AddedOnce()
    {
        var service = CreateService();

        service.AddToWhitelist(@"C:\A\**");
        service.AddToWhitelist(@"C:\A\**");

        Assert.Single(service.GetWhitelistPatterns(), p => p == @"C:\A\**");
    }

    [Fact]
    public void ValidateForDeletion_WhitelistedPath_Blocked()
    {
        var service = CreateService();

        var validation = service.ValidateForDeletion(MakeItem(@"C:\repos\myproject\.git\config"));

        Assert.False(validation.CanDelete);
        Assert.Equal("Файл в белом списке защиты", validation.BlockReason);
    }

    // ─── Per-app защита (KnowledgeBase.ProtectedPatterns) ──

    [Fact]
    public void ValidateForDeletion_PerAppProtectedPattern_BlocksDeletion()
    {
        // Интеграция с реальной базой знаний: Claude Desktop объявляет
        // ProtectedPatterns ["claude_desktop_config.json", "*.db"] для корня
        // %APPDATA%\Claude. Глобальные **\*.db сняты в 2.7.2 — per-app паттерн
        // обязан блокировать удаление такого файла.
        var config = new InMemoryConfigService([]);
        var service = new SafetyService(config, new KnowledgeBase(config));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dbFile = Path.Combine(appData, "Claude", $"puryx-h3-{Guid.NewGuid():N}.db");

        var validation = service.ValidateForDeletion(MakeItem(dbFile));

        Assert.False(validation.CanDelete);
        Assert.NotNull(validation.BlockReason);
        Assert.Contains("Claude", validation.BlockReason);
    }

    [Fact]
    public void ValidateForDeletion_UnknownDbPath_StillDeletable()
    {
        // Регресс-гард 2.7.2: глобальные **\*.db сняты, и per-app защита не
        // должна вернуть глобальный запрет — неизвестный *.db вне корней
        // известных приложений остаётся удаляемым.
        var config = new InMemoryConfigService([]);
        var service = new SafetyService(config, new KnowledgeBase(config));

        var validation = service.ValidateForDeletion(MakeItem(@"C:\SomeUnknownApp\data\cache.db"));

        Assert.True(validation.CanDelete);
        Assert.False(validation.RequiresElevation);
    }

    // ─── Protected period (только UserData) ───────

    [Fact]
    public void ValidateForDeletion_RecentUserData_BlockedByProtectedPeriod()
    {
        var service = CreateService();
        var file = Path.Combine(Path.GetTempPath(), $"safety_recent_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");
        try
        {
            var validation = service.ValidateForDeletion(MakeItem(file, RiskCategory.UserData));

            Assert.False(validation.CanDelete);
            Assert.Contains("часов", validation.BlockReason);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ValidateForDeletion_OldUserData_Allowed()
    {
        var service = CreateService();
        var file = Path.Combine(Path.GetTempPath(), $"safety_old_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");
        var oldTime = DateTime.Now.AddDays(-3);
        File.SetLastWriteTime(file, oldTime);
        try
        {
            var validation = service.ValidateForDeletion(MakeItem(file, RiskCategory.UserData));

            Assert.True(validation.CanDelete);
            Assert.False(validation.RequiresElevation);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ValidateForDeletion_RecentCache_SkipsProtectedPeriod()
    {
        var service = CreateService();
        var file = Path.Combine(Path.GetTempPath(), $"safety_cache_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");
        try
        {
            var validation = service.ValidateForDeletion(MakeItem(file, RiskCategory.PerformanceCache));

            Assert.True(validation.CanDelete);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void IsWithinProtectedPeriod_NonExistentPath_ReturnsFalse()
    {
        var service = CreateService();

        Assert.False(service.IsWithinProtectedPeriod(@"C:\definitely\missing\path\nope.txt"));
    }

    // ─── Locked files ─────────────────────────────

    [Fact]
    public void ValidateForDeletion_LockedFile_Blocked()
    {
        var service = CreateService();
        var file = Path.Combine(Path.GetTempPath(), $"safety_locked_{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "x");
        try
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Assert.True(service.IsFileLocked(file));
            var validation = service.ValidateForDeletion(MakeItem(file));
            Assert.False(validation.CanDelete);
            Assert.Equal("Файл заблокирован другим процессом", validation.BlockReason);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ValidateForDeletion_ItemFlaggedLocked_BlockedWithoutIo()
    {
        var service = CreateService();

        var validation = service.ValidateForDeletion(MakeItem(@"C:\anything\file.tmp") with { IsLocked = true });

        Assert.False(validation.CanDelete);
        Assert.Equal("Файл заблокирован другим процессом", validation.BlockReason);
    }

    // ─── RequiresElevation (регрессия префикса) ───

    [Fact]
    public void ValidateForDeletion_WindowsDirectory_RequiresElevation()
    {
        var service = CreateService();
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var inside = service.ValidateForDeletion(MakeItem(Path.Combine(windowsPath, "Temp", "file.tmp")));
        Assert.True(inside.CanDelete);
        Assert.True(inside.RequiresElevation);

        var rootItself = service.ValidateForDeletion(MakeItem(windowsPath, isDirectory: true));
        Assert.True(rootItself.CanDelete);
        Assert.True(rootItself.RequiresElevation);
    }

    [Fact]
    public void ValidateForDeletion_ProgramFiles_RequiresElevation()
    {
        var service = CreateService();
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var validation = service.ValidateForDeletion(MakeItem(Path.Combine(pf, "SomeApp", "app.dll")));

        Assert.True(validation.CanDelete);
        Assert.True(validation.RequiresElevation);
    }

    [Fact]
    public void ValidateForDeletion_WindowsFooPrefixDoesNotTriggerElevation()
    {
        // Регрессия: «C:\WindowsFoo» не должен считаться частью C:\Windows
        var service = CreateService();

        var validation = service.ValidateForDeletion(MakeItem(@"C:\WindowsFoo\readme.txt"));

        Assert.True(validation.CanDelete);
        Assert.False(validation.RequiresElevation);
    }

    [Fact]
    public void ValidateForDeletion_UserDirectory_NoElevation()
    {
        var service = CreateService();

        var validation = service.ValidateForDeletion(
            MakeItem(Path.Combine(Path.GetTempPath(), "some-file.tmp")));

        Assert.True(validation.CanDelete);
        Assert.False(validation.RequiresElevation);
    }

    // ─── Junction/reparse (findings M1, День 14 — M1 2/2) ──
    // Все проверки путей обязаны видеть РЕАЛЬНЫЙ путь цели junction,
    // а не лексический путь ссылки. Тесты создают настоящие junction
    // через cmd mklink /J (права администратора не нужны) — Integration-трейт.

    /// <summary>
    /// Создаёт junction linkPath → targetPath (cmd mklink /J).
    /// Цель может не существовать: «висячие» junction допустимы.
    /// </summary>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe не запущен");
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new InvalidOperationException($"mklink /J завис: {linkPath} -> {targetPath}");
        }
        Assert.True(Directory.Exists(linkPath),
            $"junction не создан (mklink exit={process.ExitCode}): {linkPath} -> {targetPath}");
    }

    /// <summary>
    /// Уборка тестового дерева с junction: рекурсивный Directory.Delete падает
    /// на reparse-точках, поэтому ссылки снимаются поодиночке (нерекурсивное
    /// удаление junction удаляет ссылку, не цель), затем удаляется дерево.
    /// </summary>
    private static void CleanupTree(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                    // best-effort: недоступная ссылка не должна ломать тест
                }
            }

            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // как в PathResolverTests: уборка best-effort
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ValidateForDeletion_JunctionIntoProtectedAppData_BlockedByRealPath()
    {
        // Whitelist-bypass на user-scope (findings M1): junction в temp-зоне
        // указывает на %APPDATA%\Claude; лексический путь в зоне пользователя
        // проходит все проверки, реальный путь — per-app защищён (*.db).
        var service = CreateService();
        var root = Path.Combine(Path.GetTempPath(), $"safety_junction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var claudeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            var junction = Path.Combine(root, "j");
            CreateJunction(junction, claudeRoot);
            var dbThroughJunction = Path.Combine(junction, $"puryx-d14-{Guid.NewGuid():N}.db");

            var validation = service.ValidateForDeletion(MakeItem(dbThroughJunction));

            Assert.False(validation.CanDelete,
                $"файл через junction в защищённые данные приложения не должен быть удаляемым: {dbThroughJunction}");
            Assert.NotNull(validation.BlockReason);
            Assert.Contains("Claude", validation.BlockReason);
        }
        finally
        {
            CleanupTree(root);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ValidateForDeletion_JunctionToWindows_RequiresElevationByRealPath()
    {
        // Junction в пользовательской зоне, указывающий на C:\Windows:
        // лексически elevation не требуется, реальный путь — требует.
        var service = CreateService();
        var root = Path.Combine(Path.GetTempPath(), $"safety_junction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var junction = Path.Combine(root, "w");
            CreateJunction(junction, windowsRoot);
            var itemThroughJunction = Path.Combine(junction, "Temp", "file.tmp");

            var validation = service.ValidateForDeletion(MakeItem(itemThroughJunction));

            Assert.True(validation.CanDelete);
            Assert.True(validation.RequiresElevation,
                $"путь через junction в {windowsRoot} обязан требовать elevation: {itemThroughJunction}");
        }
        finally
        {
            CleanupTree(root);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void ValidateForDeletion_CyclicJunction_BlockedFailClosed()
    {
        // Циклический junction (a→b, b→a): резолв не завершается успешно —
        // гейт обязан отказать (fail-closed), а не пропустить путь.
        var service = CreateService();
        var root = Path.Combine(Path.GetTempPath(), $"safety_junction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var a = Path.Combine(root, "a");
            var b = Path.Combine(root, "b");
            CreateJunction(a, b);
            CreateJunction(b, a);

            var validation = service.ValidateForDeletion(MakeItem(Path.Combine(a, "file.tmp")));

            Assert.False(validation.CanDelete, "путь в циклическом junction должен блокироваться");
            Assert.NotNull(validation.BlockReason);
            Assert.Contains("не удалось проверить", validation.BlockReason);
        }
        finally
        {
            CleanupTree(root);
        }
    }
}
