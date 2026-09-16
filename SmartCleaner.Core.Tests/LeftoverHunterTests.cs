using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Uninstaller;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class LeftoverHunterTests
{
    // ─── Ключевые слова ───────────────────────────

    [Fact]
    public void GenerateKeywords_StripsVersionAndEditionWords()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var app = new InstalledAppItem { DisplayName = "SuperClean 2.5 Edition Setup", Publisher = "AcmeSoft" };

        var keywords = hunter.GenerateKeywords(app);

        Assert.Contains("SuperClean 2.5 Edition Setup", keywords);
        Assert.Contains("SuperClean", keywords);
        Assert.Contains("AcmeSoft", keywords);
    }

    [Fact]
    public void GenerateKeywords_MicrosoftPublisher_IsIgnored()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var app = new InstalledAppItem { DisplayName = "SomeTool", Publisher = "Microsoft Corporation" };

        var keywords = hunter.GenerateKeywords(app);

        Assert.DoesNotContain(keywords, k => k.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MatchesAnyKeyword_MatchesExactAndPrefixedNames()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var keywords = new List<string> { "SuperClean" };

        Assert.True(hunter.MatchesAnyKeyword("SuperClean", keywords));
        Assert.True(hunter.MatchesAnyKeyword("SuperClean Pro", keywords));
        Assert.True(hunter.MatchesAnyKeyword("SuperClean-Extra", keywords));
        Assert.True(hunter.MatchesAnyKeyword("SuperClean_Data", keywords));
        Assert.False(hunter.MatchesAnyKeyword("MySuperClean", keywords));
        Assert.False(hunter.MatchesAnyKeyword("OtherApp", keywords));
    }

    [Fact]
    public void MatchesAnyKeyword_IgnoresSystemAndShortNames()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var keywords = new List<string> { "Microsoft", "ab" };

        // «Microsoft» — в игнор-листе системных папок
        Assert.False(hunter.MatchesAnyKeyword("Microsoft", keywords));
        // Слишком короткое имя папки — пропускается
        Assert.False(hunter.MatchesAnyKeyword("ab", keywords));
    }

    // ─── Очистка с safety-гейтом ──────────────────

    [Fact]
    public async Task CleanLeftoversAsync_SafetyBlockedFile_IsNotDeleted()
    {
        var hunter = new LeftoverHunter(new BlockingSafetyService());
        var file = Path.Combine(Path.GetTempPath(), $"leftover-block-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var item = new LeftoverItem
            {
                Path = file,
                Type = LeftoverType.File,
                Description = "test"
            };

            var result = await hunter.CleanLeftoversAsync([item]);

            Assert.Equal(0, result.CleanedCount);
            Assert.Single(result.SkippedMessages);
            Assert.True(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task CleanLeftoversAsync_AllowedFile_IsDeletedAndCounted()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var file = Path.Combine(Path.GetTempPath(), $"leftover-clean-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var item = new LeftoverItem
            {
                Path = file,
                Type = LeftoverType.File,
                SizeBytes = 4,
                Description = "test"
            };

            var result = await hunter.CleanLeftoversAsync([item]);

            Assert.Equal(1, result.CleanedCount);
            Assert.Equal(4, result.SavedBytes);
            Assert.Empty(result.SkippedMessages);
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact]
    public async Task CleanLeftoversAsync_GitFolderBlockedByRealWhitelist_IsNotDeleted()
    {
        // Интеграционный сценарий: настоящий SafetyService + встроенный паттерн **\.git\**
        var hunter = new LeftoverHunter(new SafetyService(new InMemoryConfigService([])));
        var root = Path.Combine(Path.GetTempPath(), $"leftover-git-{Guid.NewGuid():N}");
        var gitDir = Path.Combine(root, ".git");
        Directory.CreateDirectory(gitDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/main");
            var item = new LeftoverItem
            {
                Path = gitDir,
                Type = LeftoverType.Folder,
                Description = "test"
            };

            var result = await hunter.CleanLeftoversAsync([item]);

            Assert.Equal(0, result.CleanedCount);
            Assert.True(Directory.Exists(gitDir));
            Assert.True(File.Exists(Path.Combine(gitDir, "HEAD")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CleanLeftoversAsync_UnselectedItems_AreIgnored()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var file = Path.Combine(Path.GetTempPath(), $"leftover-unselected-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var item = new LeftoverItem
            {
                Path = file,
                Type = LeftoverType.File,
                IsSelected = false,
                Description = "test"
            };

            var result = await hunter.CleanLeftoversAsync([item]);

            Assert.Equal(0, result.CleanedCount);
            Assert.True(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task CleanLeftoversAsync_MissingPath_IsSkippedWithoutError()
    {
        var hunter = new LeftoverHunter(new AllowAllSafetyService());
        var item = new LeftoverItem
        {
            Path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
            Type = LeftoverType.File,
            Description = "test"
        };

        var result = await hunter.CleanLeftoversAsync([item]);

        Assert.Equal(0, result.CleanedCount);
        Assert.Empty(result.SkippedMessages);
    }
}
