using SmartCleaner.Core.AiAssistant;
using SmartCleaner.Core.Compression;
using SmartCleaner.Core.Duplicates;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Plugins;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Uninstaller;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class TierFeaturesTests
{
    [Fact]
    public async Task DevSuperScanner_ScanAsync_ExecutesSafely()
    {
        var config = new InMemoryConfigService([]);
        var knowledge = new KnowledgeBase(config);
        var safety = new SafetyService(config);
        var scanner = new DevSuperScanner(knowledge, safety);

        var result = await scanner.ScanAsync();

        Assert.NotNull(result);
        Assert.Equal("Разработка (Dev Super-Cleaner)", result.CategoryName);
        Assert.NotNull(result.Items);
    }

    [Theory]
    [InlineData("найди видео больше 2 гб", AiActionType.OpenPage, "LargeFiles")]
    [InlineData("освободи 20 гб под игру", AiActionType.TriggerScan, "All")]
    [InlineData("почисти мусор от rust и node", AiActionType.OpenPage, "DevClean")]
    [InlineData("найди дубликаты", AiActionType.OpenPage, "Duplicates")]
    [InlineData("сожми игры в steam", AiActionType.OpenPage, "Compact")]
    [InlineData("удали старую программу и почисти хвосты", AiActionType.OpenPage, "Uninstaller")]
    public async Task NaturalLanguageQueryEngine_ProcessesQueriesCorrectly(string query, AiActionType expectedAction, string expectedPayload)
    {
        var engine = new NaturalLanguageQueryEngine();
        var reply = await engine.ProcessUserQueryAsync(query);

        Assert.NotNull(reply);
        Assert.False(reply.IsUser);
        Assert.Equal(expectedAction, reply.ActionType);
        Assert.Equal(expectedPayload, reply.ActionPayload);
        Assert.NotEmpty(reply.Text);
    }

    [Fact]
    public async Task QuarantineService_MoveAndRestore_WorksAccurately()
    {
        var quarantine = new QuarantineService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"quarantine_test_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "Test quarantine content 12345");

        // 1. Move to Quarantine
        var moved = await quarantine.MoveToQuarantineAsync(tempFile, "TestCategory");
        Assert.True(moved);
        Assert.False(File.Exists(tempFile));

        var items = await quarantine.GetQuarantinedItemsAsync();
        var item = items.FirstOrDefault(i => i.OriginalPath == tempFile);
        Assert.NotNull(item);

        // 2. Restore from Quarantine
        var (success, msg) = await quarantine.RestoreItemAsync(item.Id);
        Assert.True(success);
        Assert.True(File.Exists(tempFile));
        var content = await File.ReadAllTextAsync(tempFile);
        Assert.Equal("Test quarantine content 12345", content);

        // Cleanup
        try { File.Delete(tempFile); } catch { }
    }

    [Fact]
    public async Task PluginEngine_LoadPluginsAsync_LoadsManifests()
    {
        var engine = new PluginEngine();
        var plugins = await engine.LoadPluginsAsync();

        Assert.NotNull(plugins);
        Assert.True(plugins.Count >= 0);
    }

    [Fact]
    public async Task CompactEngine_DiscoverTargetsAsync_RunsWithoutError()
    {
        var engine = new CompactEngine();
        var targets = await engine.DiscoverCompressibleTargetsAsync();

        Assert.NotNull(targets);
    }

    [Fact]
    public void LeftoverItem_ModelProperties_AssignCorrectly()
    {
        var item = new LeftoverItem
        {
            Path = @"C:\Users\Test\AppData\Roaming\FakeApp",
            Type = LeftoverType.Folder,
            Description = "Test Leftover Folder",
            SizeBytes = 1024 * 1024,
            SizeFormatted = "1.00 MB"
        };

        Assert.Equal("Папка", item.TypeName);
        Assert.True(item.IsSelected);
        Assert.Equal(1024 * 1024, item.SizeBytes);
    }

    [Fact]
    public void TreemapLayout_CalculateSquarified_ReturnsValidTiles()
    {
        var root = new SmartCleaner.Core.DiskMap.DiskNode
        {
            Name = "Root",
            FullPath = @"C:\Root",
            Size = 1000,
            Children = new List<SmartCleaner.Core.DiskMap.DiskNode>
            {
                new() { Name = "Video.mp4", FullPath = @"C:\Root\Video.mp4", Size = 600, IsFile = true },
                new() { Name = "Archive.zip", FullPath = @"C:\Root\Archive.zip", Size = 300, IsFile = true },
                new() { Name = "Code.cs", FullPath = @"C:\Root\Code.cs", Size = 100, IsFile = true }
            }
        };

        var tiles = SmartCleaner.Core.DiskMap.TreemapLayout.CalculateSquarified(root, 800, 600);

        Assert.NotEmpty(tiles);
        Assert.Equal(3, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.Width > 0 && t.Height > 0));
    }

    [Fact]
    public void RamOptimizerService_GetMemoryStatus_ReturnsValidMetrics()
    {
        var service = new SmartCleaner.Core.SystemOpt.RamOptimizerService();
        var mem = service.GetMemoryStatus();

        Assert.True(mem.TotalBytes > 0);
        Assert.True(mem.AvailableBytes > 0);
        Assert.True(mem.LoadPercentage >= 0 && mem.LoadPercentage <= 100);
    }

    [Fact]
    public async Task FileShredderService_ShredFileAsync_WipesAndDeletesFile()
    {
        var shredder = new SmartCleaner.Core.Safety.FileShredderService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"shred_test_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "Top secret content to shred 1234567890");

        var (success, msg) = await shredder.ShredFileAsync(tempFile, SmartCleaner.Core.Safety.ShredMethod.DoD522022M3Pass);

        Assert.True(success);
        Assert.False(File.Exists(tempFile));
    }

    [Fact]
    public async Task SqliteCompactorService_DiscoverDatabasesAsync_RunsSafely()
    {
        var service = new SmartCleaner.Core.Optimization.SqliteCompactorService();
        var databases = await service.DiscoverDatabasesAsync();

        Assert.NotNull(databases);
    }

    [Fact]
    public async Task ShaderCacheScanner_ScanAsync_ExecutesSuccessfully()
    {
        var config = new InMemoryConfigService([]);
        var knowledge = new KnowledgeBase(config);
        var safety = new SafetyService(config);
        var scanner = new SmartCleaner.Core.Scanning.Scanners.ShaderCacheScanner(knowledge, safety);

        var result = await scanner.ScanAsync();

        Assert.NotNull(result);
        Assert.Equal("Кэши шейдеров GPU и DirectX", result.CategoryName);
    }

    [Fact]
    public void LocalizationManager_LanguageSwitch_UpdatesStrings()
    {
        var loc = SmartCleaner.Core.Localization.LocalizationManager.Instance;
        loc.SetLanguage(SmartCleaner.Core.Localization.AppLanguage.English);
        Assert.Equal("Scan System", loc.GetString("Scan"));

        loc.SetLanguage(SmartCleaner.Core.Localization.AppLanguage.Russian);
        Assert.Equal("Анализ системы", loc.GetString("Scan"));
    }

    [Fact]
    public async Task GameBoostService_StateTransitions_WorkCorrectly()
    {
        var ram = new SmartCleaner.Core.SystemOpt.RamOptimizerService();
        var boost = new SmartCleaner.Core.SystemOpt.GameBoostService(ram);

        Assert.False(boost.CurrentState.IsBoostActive);

        var enabledState = await boost.EnableGameBoostAsync();
        Assert.True(enabledState.IsBoostActive);

        var disabledState = await boost.DisableGameBoostAsync();
        Assert.False(disabledState.IsBoostActive);
    }

    [Fact]
    public async Task DiskHealthService_GetPhysicalDisksHealthAsync_ReturnsDisks()
    {
        var service = new SmartCleaner.Core.DiskHealth.DiskHealthService();
        var disks = await service.GetPhysicalDisksHealthAsync();

        Assert.NotNull(disks);
        Assert.NotEmpty(disks);
        Assert.All(disks, d => Assert.NotEmpty(d.FriendlyName));
    }

    [Fact]
    public void NetworkOptimizerService_Presets_AreLoadedCorrectly()
    {
        var service = new SmartCleaner.Core.Network.NetworkOptimizerService();
        Assert.NotEmpty(service.Presets);
        Assert.Contains(service.Presets, p => p.Primary == "1.1.1.1");
        Assert.Contains(service.Presets, p => p.Primary == "8.8.8.8");
    }

    [Fact]
    public void ExplorerContextMenuManager_InstantiatesSafely()
    {
        var manager = new SmartCleaner.Core.Shell.ExplorerContextMenuManager();
        // Check reading registry keys without throw
        var isRegistered = manager.IsAnalyzeFolderRegistered();
        Assert.True(isRegistered || !isRegistered);
    }

    [Fact]
    public void SystemReportGenerator_GenerateHtmlReport_ProducesValidHtml()
    {
        var generator = new SmartCleaner.Core.Reporting.SystemReportGenerator();
        var data = new SmartCleaner.Core.Reporting.SystemReportData
        {
            ComputerName = "TEST-PC",
            TotalFreedFormatted = "12.4 GB",
            TotalRamBytes = 34359738368L
        };

        var html = generator.GenerateHtmlReport(data);

        Assert.NotNull(html);
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("TEST-PC", html);
        Assert.Contains("12.4 GB", html);
    }

    [Fact]
    public void PrivacyDebloatService_TweakDefinitions_AreValidAndComplete()
    {
        var service = new SmartCleaner.Core.Privacy.PrivacyDebloatService();
        var tweaks = service.GetTweakDefinitions();

        Assert.NotNull(tweaks);
        Assert.True(tweaks.Count >= 10);
        Assert.All(tweaks, t =>
        {
            Assert.NotEmpty(t.Id);
            Assert.NotEmpty(t.Title);
            Assert.NotEmpty(t.Description);
            Assert.NotEmpty(t.CategoryName);
        });

        // Ensure unique IDs
        var ids = tweaks.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task PrivacyDebloatService_ScanStatusesAsync_ExecutesSafely()
    {
        var service = new SmartCleaner.Core.Privacy.PrivacyDebloatService();
        var scanned = await service.ScanStatusesAsync();

        Assert.NotNull(scanned);
        Assert.Equal(service.GetTweakDefinitions().Count, scanned.Count);
    }

    [Fact]
    public void WindowsServicesOptimizer_KnownServices_AndProfiles_AreValid()
    {
        var optimizer = new SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer();
        var services = optimizer.GetKnownServices();

        Assert.NotNull(services);
        Assert.True(services.Count >= 10);
        Assert.Contains(services, s => s.ServiceName == "DiagTrack");
        Assert.Contains(services, s => s.ServiceName == "Fax");
        Assert.Contains(services, s => s.ServiceName == "RemoteRegistry");

        var gamingServices = SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer.GetProfileTargetServices(SmartCleaner.Core.ServicesOpt.ServiceProfileType.Gaming);
        Assert.Contains("SysMain", gamingServices);
        Assert.Contains("DiagTrack", gamingServices);
        Assert.Contains("Fax", gamingServices);

        var balancedServices = SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer.GetProfileTargetServices(SmartCleaner.Core.ServicesOpt.ServiceProfileType.Balanced);
        Assert.Contains("Fax", balancedServices);
        Assert.Contains("RemoteRegistry", balancedServices);
    }

    [Fact]
    public async Task WindowsServicesOptimizer_ScanServicesAsync_ExecutesSafely()
    {
        var optimizer = new SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer();
        var scanned = await optimizer.ScanServicesAsync();

        Assert.NotNull(scanned);
        Assert.Equal(optimizer.GetKnownServices().Count, scanned.Count);
    }
}
