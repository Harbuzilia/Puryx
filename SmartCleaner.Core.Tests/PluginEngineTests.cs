using SmartCleaner.Core.Plugins;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class PluginEngineTests
{
    private static PluginManifest MakePlugin(params string[] pathTemplates)
    {
        return new PluginManifest
        {
            Id = "test-plugin",
            Name = "Test Plugin",
            Rules = pathTemplates.Select(t => new PluginTargetRule { PathTemplate = t }).ToList()
        };
    }

    [Fact]
    public async Task ScanPluginItemsAsync_MultiSegmentWildcard_ResolvesAllMatchingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"plugin-scan-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "JetBrains", "ProductA", "caches"));
            Directory.CreateDirectory(Path.Combine(root, "JetBrains", "ProductB", "caches"));
            Directory.CreateDirectory(Path.Combine(root, "JetBrains", "ProductB", "other"));
            await File.WriteAllTextAsync(Path.Combine(root, "JetBrains", "ProductA", "caches", "a.dat"), new string('a', 100));
            await File.WriteAllTextAsync(Path.Combine(root, "JetBrains", "ProductB", "caches", "b.dat"), new string('b', 200));
            await File.WriteAllTextAsync(Path.Combine(root, "JetBrains", "ProductB", "other", "c.dat"), new string('c', 300));

            var engine = new PluginEngine(new AllowAllSafetyService());
            var plugin = MakePlugin(Path.Combine(root, "JetBrains", "*", "caches"));

            var items = await engine.ScanPluginItemsAsync([plugin]);

            Assert.Equal(2, items.Count);
            Assert.Contains(items, i => i.Path.EndsWith(Path.Combine("ProductA", "caches"), StringComparison.OrdinalIgnoreCase));
            Assert.Contains(items, i => i.Path.EndsWith(Path.Combine("ProductB", "caches"), StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(items, i => i.Path.Contains("other"));
            Assert.All(items, i => Assert.Equal("test-plugin", i.PluginId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanPluginItemsAsync_LiteralPathWithoutStar_ReturnsExistingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"plugin-literal-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "caches"));
            await File.WriteAllTextAsync(Path.Combine(root, "caches", "x.dat"), new string('x', 50));

            var engine = new PluginEngine(new AllowAllSafetyService());
            var plugin = MakePlugin(Path.Combine(root, "caches"));

            var items = await engine.ScanPluginItemsAsync([plugin]);

            var item = Assert.Single(items);
            Assert.Equal(50, item.SizeBytes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScanPluginItemsAsync_DisabledPlugin_IsSkipped()
    {
        var engine = new PluginEngine(new AllowAllSafetyService());
        var plugin = MakePlugin(Path.Combine(Path.GetTempPath(), "whatever"));
        plugin.IsEnabled = false;

        var items = await engine.ScanPluginItemsAsync([plugin]);

        Assert.Empty(items);
    }

    [Fact]
    public async Task CleanPluginItemsAsync_SafetyBlockedItem_IsNotDeleted()
    {
        var file = Path.Combine(Path.GetTempPath(), $"plugin-block-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var engine = new PluginEngine(new BlockingSafetyService());
            var item = new PluginScanItem
            {
                PluginId = "test-plugin",
                PluginName = "Test Plugin",
                Path = file,
                SizeBytes = 4
            };

            var result = await engine.CleanPluginItemsAsync([item]);

            Assert.Equal(0, result.CleanedCount);
            Assert.Equal(0, result.SavedBytes);
            Assert.Single(result.SkippedMessages);
            Assert.True(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task CleanPluginItemsAsync_AllowedItem_IsDeletedWithSavedBytes()
    {
        var file = Path.Combine(Path.GetTempPath(), $"plugin-clean-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var engine = new PluginEngine(new AllowAllSafetyService());
            var item = new PluginScanItem
            {
                PluginId = "test-plugin",
                PluginName = "Test Plugin",
                Path = file,
                SizeBytes = 4
            };

            var result = await engine.CleanPluginItemsAsync([item]);

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
    public async Task CleanPluginItemsAsync_UnselectedItem_IsSkipped()
    {
        var file = Path.Combine(Path.GetTempPath(), $"plugin-unselected-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, "data");
        try
        {
            var engine = new PluginEngine(new AllowAllSafetyService());
            var item = new PluginScanItem
            {
                PluginId = "test-plugin",
                PluginName = "Test Plugin",
                Path = file,
                SizeBytes = 4,
                IsSelected = false
            };

            var result = await engine.CleanPluginItemsAsync([item]);

            Assert.Equal(0, result.CleanedCount);
            Assert.True(File.Exists(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
