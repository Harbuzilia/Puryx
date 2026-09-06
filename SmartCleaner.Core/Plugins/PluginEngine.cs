using SmartCleaner.Core.Helpers;
using System.IO;
using System.Text.Json;

namespace SmartCleaner.Core.Plugins;

public class PluginScanItem
{
    public string PluginId { get; set; } = string.Empty;
    public string PluginName { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public bool IsSelected { get; set; } = true;
}

public class PluginEngine
{
    public async Task<List<PluginManifest>> LoadPluginsAsync()
    {
        var plugins = new List<PluginManifest>();

        var searchDirs = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "SmartCleaner.Data", "Plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartCleaner", "Plugins")
        };

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.plugin.json"))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file);
                        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (manifest != null && !plugins.Any(p => p.Id == manifest.Id))
                        {
                            plugins.Add(manifest);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return plugins;
    }

    public async Task<List<PluginScanItem>> ScanPluginItemsAsync(IEnumerable<PluginManifest> plugins, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<PluginScanItem>();

        await Task.Run(() =>
        {
            foreach (var plugin in plugins.Where(p => p.IsEnabled))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Сканирование плагина: {plugin.Name}...");

                foreach (var rule in plugin.Rules)
                {
                    var resolvedPath = Environment.ExpandEnvironmentVariables(rule.PathTemplate);
                    if (resolvedPath.Contains('*'))
                    {
                        var parentDir = Path.GetDirectoryName(resolvedPath.Split('*')[0]);
                        var pattern = resolvedPath.Substring(parentDir?.Length ?? 0).TrimStart('\\', '/');

                        if (Directory.Exists(parentDir))
                        {
                            try
                            {
                                foreach (var matchingDir in Directory.EnumerateDirectories(parentDir, pattern, SearchOption.TopDirectoryOnly))
                                {
                                    AddTargetItem(results, plugin, matchingDir);
                                }
                            }
                            catch { }
                        }
                    }
                    else if (Directory.Exists(resolvedPath))
                    {
                        AddTargetItem(results, plugin, resolvedPath);
                    }
                }
            }
        }, ct);

        return results;
    }

    public async Task<(int CleanedCount, long SavedBytes)> CleanPluginItemsAsync(IEnumerable<PluginScanItem> items, IProgress<string>? progress = null)
    {
        int count = 0;
        long saved = 0;

        await Task.Run(() =>
        {
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    if (Directory.Exists(item.Path))
                    {
                        progress?.Report($"Очистка {item.PluginName} -> {Path.GetFileName(item.Path)}...");
                        Directory.Delete(item.Path, true);
                        count++;
                        saved += item.SizeBytes;
                    }
                    else if (File.Exists(item.Path))
                    {
                        File.Delete(item.Path);
                        count++;
                        saved += item.SizeBytes;
                    }
                }
                catch { }
            }
        });

        return (count, saved);
    }

    private void AddTargetItem(List<PluginScanItem> results, PluginManifest plugin, string dir)
    {
        try
        {
            var size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });

            if (size > 0)
            {
                results.Add(new PluginScanItem
                {
                    PluginId = plugin.Id,
                    PluginName = plugin.Name,
                    Path = dir,
                    SizeBytes = size,
                    SizeFormatted = SizeFormatter.Format(size),
                    IsSelected = true
                });
            }
        }
        catch { }
    }
}
