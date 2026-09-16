using Microsoft.Win32;
using Microsoft.VisualBasic.FileIO;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using System.IO;

namespace SmartCleaner.Core.Uninstaller;

/// <summary>
/// Результат очистки хвостов: содержит только реально удалённые элементы
/// </summary>
public record LeftoverCleanResult
{
    public int CleanedCount { get; init; }
    public long SavedBytes { get; init; }
    public IReadOnlyList<LeftoverItem> CleanedItems { get; init; } = [];
    public IReadOnlyList<string> SkippedMessages { get; init; } = [];
}

/// <summary>
/// Охотник за остаточными файлами (хвостами) программ в реестре, AppData, ProgramData
/// </summary>
public class LeftoverHunter
{
    private readonly ISafetyService _safety;

    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    public LeftoverHunter(ISafetyService safety)
    {
        _safety = safety;
    }

    public async Task<List<LeftoverItem>> FindLeftoversAsync(InstalledAppItem app, CancellationToken ct = default)
    {
        var leftovers = new List<LeftoverItem>();
        if (app == null || string.IsNullOrWhiteSpace(app.DisplayName))
            return leftovers;

        var searchKeywords = GenerateKeywords(app);

        await Task.Run(() =>
        {
            // 1. Scan Folders in AppData & ProgramData
            var searchRoots = new[]
            {
                LocalAppData,
                RoamingAppData,
                ProgramData,
                Path.Combine(UserProfile, "Documents"),
                Path.Combine(UserProfile, "Saved Games")
            };

            foreach (var root in searchRoots)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(root)) continue;

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (MatchesAnyKeyword(dirName, searchKeywords))
                        {
                            var size = GetDirSizeSafe(dir);
                            leftovers.Add(new LeftoverItem
                            {
                                Path = dir,
                                Type = LeftoverType.Folder,
                                Description = $"Остаточная папка в {Path.GetFileName(root)}",
                                SizeBytes = size,
                                SizeFormatted = SizeFormatter.Format(size),
                                IsSelected = true
                            });
                        }
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LeftoverHunter] FindLeftovers dir enumeration error: {ex.Message}"); }
            }

            // 2. Scan Registry
            ScanRegistryKeys(Registry.CurrentUser, @"Software", searchKeywords, leftovers);
            ScanRegistryKeys(Registry.LocalMachine, @"SOFTWARE", searchKeywords, leftovers);
            ScanRegistryKeys(Registry.LocalMachine, @"SOFTWARE\WOW6432Node", searchKeywords, leftovers);

        }, ct);

        return leftovers;
    }

    public async Task<LeftoverCleanResult> CleanLeftoversAsync(IEnumerable<LeftoverItem> items)
    {
        var cleaned = new List<LeftoverItem>();
        var skipped = new List<string>();
        long space = 0;

        await Task.Run(() =>
        {
            foreach (var item in items.Where(i => i.IsSelected))
            {
                try
                {
                    if (item.Type == LeftoverType.Folder && Directory.Exists(item.Path))
                    {
                        if (!ValidateAndReport(item, skipped)) continue;

                        FileSystem.DeleteDirectory(
                            item.Path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                        cleaned.Add(item);
                        space += item.SizeBytes;
                    }
                    else if (item.Type == LeftoverType.File && File.Exists(item.Path))
                    {
                        if (!ValidateAndReport(item, skipped)) continue;

                        FileSystem.DeleteFile(
                            item.Path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                        cleaned.Add(item);
                        space += item.SizeBytes;
                    }
                    else if (item.Type == LeftoverType.RegistryKey)
                    {
                        var parts = item.Path.Split('\\', 2);
                        if (parts.Length == 2)
                        {
                            var root = parts[0] == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;
                            root.DeleteSubKeyTree(parts[1], false);
                            cleaned.Add(item);
                        }
                    }
                }
                catch (Exception ex) { skipped.Add($"{item.Path}: {ex.Message}"); }
            }
        });

        return new LeftoverCleanResult
        {
            CleanedCount = cleaned.Count,
            SavedBytes = space,
            CleanedItems = cleaned,
            SkippedMessages = skipped
        };
    }

    /// <summary>
    /// Прогоняет путь через ISafetyService (whitelist, блокировки, права) и регистрирует отказ
    /// </summary>
    private bool ValidateAndReport(LeftoverItem item, List<string> skipped)
    {
        var validation = _safety.ValidateForDeletion(new ScannedItem
        {
            Path = item.Path,
            IsDirectory = item.Type == LeftoverType.Folder,
            Size = item.SizeBytes,
            Risk = RiskCategory.PerformanceCache,
            Description = $"leftover:{item.Description}"
        });

        if (validation.CanDelete && !validation.RequiresElevation) return true;

        var reason = validation.BlockReason ?? (validation.RequiresElevation
            ? "Требуются права администратора"
            : "Заблокировано службой безопасности");
        skipped.Add($"{item.Path}: {reason}");
        return false;
    }

    private void ScanRegistryKeys(RegistryKey rootKey, string subPath, List<string> keywords, List<LeftoverItem> leftovers)
    {
        try
        {
            using var baseKey = rootKey.OpenSubKey(subPath);
            if (baseKey == null) return;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                if (MatchesAnyKeyword(subName, keywords))
                {
                    var prefix = rootKey == Registry.CurrentUser ? "HKCU" : "HKLM";
                    leftovers.Add(new LeftoverItem
                    {
                        Path = $@"{prefix}\{subPath}\{subName}",
                        Type = LeftoverType.RegistryKey,
                        Description = $"Раздел реестра {subName}",
                        SizeBytes = 0,
                        SizeFormatted = "Реестр",
                        IsSelected = true
                    });
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LeftoverHunter] Registry scan error: {ex.Message}"); }
    }

    internal List<string> GenerateKeywords(InstalledAppItem app)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(app.DisplayName))
        {
            list.Add(app.DisplayName.Trim());
            // Filter version numbers or words like "Edition", "Setup"
            var clean = System.Text.RegularExpressions.Regex.Replace(app.DisplayName, @"(?i)\b(v?\d+(\.\d+)*|edition|setup|installer|x64|x86|64-bit|32-bit)\b", "").Trim();
            if (!string.IsNullOrWhiteSpace(clean) && clean.Length >= 3)
            {
                list.Add(clean);
            }
        }
        if (!string.IsNullOrWhiteSpace(app.Publisher) && app.Publisher.Length >= 3 && !app.Publisher.Contains("Microsoft"))
        {
            list.Add(app.Publisher.Trim());
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal bool MatchesAnyKeyword(string name, List<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length < 3) return false;
        
        // System and generic directories to ignore
        var ignore = new[] { "Microsoft", "Windows", "Common Files", "Default", "System", "Temp", "Packages", "DirectX", "NVIDIA Corporation", "Intel" };
        if (ignore.Any(ig => ig.Equals(name, StringComparison.OrdinalIgnoreCase))) return false;

        foreach (var kw in keywords)
        {
            if (name.Equals(kw, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(kw + " ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(kw + "-", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(kw + "_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private long GetDirSizeSafe(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LeftoverHunter] File size error: {ex.Message}"); return 0; } });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LeftoverHunter] GetDirSizeSafe error: {ex.Message}"); return 0; }
    }
}
