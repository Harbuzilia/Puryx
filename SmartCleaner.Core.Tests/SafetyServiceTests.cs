using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
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
}
