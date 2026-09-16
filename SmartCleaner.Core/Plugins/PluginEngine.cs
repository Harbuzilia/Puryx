using Microsoft.VisualBasic.FileIO;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using System.Diagnostics;
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

public class PluginCleanResult
{
    public int CleanedCount { get; init; }
    public long SavedBytes { get; init; }
    public IReadOnlyList<PluginScanItem> CleanedItems { get; init; } = [];
    public IReadOnlyList<string> SkippedMessages { get; init; } = [];
}

public class PluginEngine
{
    private readonly ISafetyService _safety;

    public PluginEngine(ISafetyService safety)
    {
        _safety = safety;
    }

    public async Task<List<PluginManifest>> LoadPluginsAsync()
    {
        var plugins = new List<PluginManifest>();

        // Встроенные плагины лежат рядом с приложением (копируются сборкой),
        // пользовательские — в %APPDATA%\SmartCleaner\Plugins.
        var searchDirs = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"),
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
                        if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id) && !plugins.Any(p => p.Id == manifest.Id))
                        {
                            plugins.Add(manifest);
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[PluginEngine] Plugin JSON parse error: {ex.Message}"); }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[PluginEngine] Plugin dir enumeration error: {ex.Message}"); }
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
                    foreach (var targetPath in ResolveRuleTargets(resolvedPath))
                    {
                        AddTargetItem(results, plugin, targetPath);
                    }
                }
            }
        }, ct);

        return results;
    }

    public async Task<PluginCleanResult> CleanPluginItemsAsync(IEnumerable<PluginScanItem> items, IProgress<string>? progress = null)
    {
        var cleanedItems = new List<PluginScanItem>();
        var skippedMessages = new List<string>();
        long saved = 0;

        await Task.Run(() =>
        {
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    if (!Directory.Exists(item.Path) && !File.Exists(item.Path))
                    {
                        continue;
                    }

                    var isDirectory = Directory.Exists(item.Path);
                    var validation = _safety.ValidateForDeletion(new ScannedItem
                    {
                        Path = item.Path,
                        IsDirectory = isDirectory,
                        Size = item.SizeBytes,
                        Risk = RiskCategory.PerformanceCache,
                        Description = $"plugin:{item.PluginId}"
                    });

                    if (!validation.CanDelete)
                    {
                        skippedMessages.Add($"{item.Path}: {validation.BlockReason ?? "Заблокировано политикой безопасности"}");
                        continue;
                    }

                    if (validation.RequiresElevation)
                    {
                        skippedMessages.Add($"{item.Path}: требует прав администратора, пропущено");
                        continue;
                    }

                    progress?.Report($"Очистка {item.PluginName} -> {Path.GetFileName(item.Path)}...");

                    // Плагины — сторонний код, поэтому удаление только в Корзину, без перманентного режима.
                    if (isDirectory)
                    {
                        FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }

                    cleanedItems.Add(item);
                    saved += item.SizeBytes;
                }
                catch (Exception ex) { Debug.WriteLine($"[PluginEngine] Clean item error: {ex.Message}"); }
            }
        });

        return new PluginCleanResult
        {
            CleanedCount = cleanedItems.Count,
            SavedBytes = saved,
            CleanedItems = cleanedItems,
            SkippedMessages = skippedMessages
        };
    }

    /// <summary>
    /// Резолвит шаблон правила в конкретные каталоги. Поддерживает '*' в любом сегменте
    /// (например, %LOCALAPPDATA%\JetBrains\*\caches — '*' в середине пути).
    /// </summary>
    private static IEnumerable<string> ResolveRuleTargets(string resolvedPath)
    {
        if (!resolvedPath.Contains('*'))
        {
            if (Directory.Exists(resolvedPath))
            {
                yield return resolvedPath;
            }
            yield break;
        }

        var starIndex = resolvedPath.IndexOf('*');
        var fixedPrefix = resolvedPath[..starIndex];
        var rootDir = Path.GetDirectoryName(fixedPrefix.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(rootDir) || !Directory.Exists(rootDir))
        {
            yield break;
        }

        var remainingPattern = resolvedPath[rootDir.Length..].TrimStart('\\', '/');

        foreach (var match in EnumeratePatternMatches(rootDir, remainingPattern))
        {
            yield return match;
        }
    }

    private static IEnumerable<string> EnumeratePatternMatches(string currentDir, string remainingPattern)
    {
        var separatorIndex = remainingPattern.IndexOfAny(['\\', '/']);
        var currentPattern = separatorIndex < 0 ? remainingPattern : remainingPattern[..separatorIndex];
        var rest = separatorIndex < 0 ? null : remainingPattern[(separatorIndex + 1)..];

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateDirectories(currentDir, currentPattern, System.IO.SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PluginEngine] Rule dir enumeration error: {ex.Message}");
            yield break;
        }

        foreach (var match in matches)
        {
            if (rest is null)
            {
                yield return match;
            }
            else
            {
                foreach (var subMatch in EnumeratePatternMatches(match, rest))
                {
                    yield return subMatch;
                }
            }
        }
    }

    private void AddTargetItem(List<PluginScanItem> results, PluginManifest plugin, string dir)
    {
        try
        {
            var size = Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch (Exception ex) { Debug.WriteLine($"[PluginEngine] File size error: {ex.Message}"); return 0; } });

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
        catch (Exception ex) { Debug.WriteLine($"[PluginEngine] AddTargetItem error: {ex.Message}"); }
    }
}
