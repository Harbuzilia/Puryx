using System.Text.Json;
using SmartCleaner.Core.Plugins;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class PluginManifestTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Deserialize_MinimalManifest_DefaultsApplied()
    {
        var json = """
            {
                "id": "test-plugin-1",
                "name": "Test Plugin"
            }
            """;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

        Assert.NotNull(manifest);
        Assert.Equal("test-plugin-1", manifest.Id);
        Assert.Equal("Test Plugin", manifest.Name);
        Assert.Equal("Community", manifest.Author);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.True(manifest.IsEnabled);
        Assert.Empty(manifest.Rules);
    }

    [Fact]
    public void Deserialize_FullManifest_AllFieldsSet()
    {
        var json = """
            {
                "id": "clean-cache-1",
                "name": "Cache Cleaner",
                "author": "SmartCleaner Team",
                "version": "2.1.0",
                "description": "Cleans application caches",
                "category": "Optimization",
                "icon": "\uE123",
                "isEnabled": false,
                "rules": [
                    {
                        "pathTemplate": "%LOCALAPPDATA%\\Temp\\cache_*",
                        "pattern": "*.tmp",
                        "recursive": false,
                        "excludes": [".gitkeep"]
                    }
                ]
            }
            """;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

        Assert.NotNull(manifest);
        Assert.Equal("clean-cache-1", manifest.Id);
        Assert.Equal("Cache Cleaner", manifest.Name);
        Assert.Equal("SmartCleaner Team", manifest.Author);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal("Cleans application caches", manifest.Description);
        Assert.Equal("Optimization", manifest.Category);
        Assert.False(manifest.IsEnabled);
        Assert.Single(manifest.Rules);

        var rule = manifest.Rules[0];
        Assert.Equal("%LOCALAPPDATA%\\Temp\\cache_*", rule.PathTemplate);
        Assert.Equal("*.tmp", rule.Pattern);
        Assert.False(rule.Recursive);
        Assert.Contains(".gitkeep", rule.Excludes);
    }

    [Fact]
    public void Deserialize_MultipleRules_AllLoaded()
    {
        var json = """
            {
                "id": "multi-rule",
                "name": "Multi Rule",
                "rules": [
                    { "pathTemplate": "%TEMP%\\a" },
                    { "pathTemplate": "%TEMP%\\b" },
                    { "pathTemplate": "%TEMP%\\c" }
                ]
            }
            """;
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);

        Assert.NotNull(manifest);
        Assert.Equal(3, manifest.Rules.Count);
    }

    [Fact]
    public void Deserialize_InvalidJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<PluginManifest>("{invalid json}", JsonOpts));
    }

    [Fact]
    public void PluginScanItem_Defaults_IsSelected()
    {
        var item = new PluginScanItem();
        Assert.True(item.IsSelected);
        Assert.Equal("0 B", item.SizeFormatted);
        Assert.Empty(item.PluginId);
        Assert.Empty(item.PluginName);
        Assert.Empty(item.Path);
    }

    [Fact]
    public void PluginTargetRule_Defaults_RecursiveTrue()
    {
        var rule = new PluginTargetRule();
        Assert.True(rule.Recursive);
        Assert.Equal("*", rule.Pattern);
        Assert.Empty(rule.PathTemplate);
        Assert.Empty(rule.Excludes);
    }
}