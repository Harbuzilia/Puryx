using Microsoft.Extensions.Logging;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using System.IO;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// S-Tier Dev Super-Cleaner: сканирует кэши сборки и виртуальные диски разработки
/// (WSL2 ext4.vhdx, Rust cargo/target, Gradle, Maven, Go, Android SDK, Unity/Unreal DDC)
/// </summary>
public class DevSuperScanner : ScannerBase
{
    public override string CategoryName => "Разработка (Dev Super-Cleaner)";
    public override string CategoryIcon => "\uE7B8"; // Developer Tools icon
    public override int DisplayOrder => 5;

    public DevSuperScanner(ISafetyService safety, ILogger<DevSuperScanner>? logger = null)
        : base(safety, logger)
    {
    }

    /// <summary>
    /// Корень прямых установок WSL2 (%LOCALAPPDATA%\wsl). Virtual — шов для
    /// тестов (скан temp-дерева с junction, День 14).
    /// </summary>
    protected virtual string DirectWslDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "wsl");

    /// <summary>
    /// Корень AVD-эмуляторов Android (%USERPROFILE%\.android\avd). Virtual — шов
    /// для тестов.
    /// </summary>
    protected virtual string AndroidAvdSnapshotsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");

    public override Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        progress?.Report(Localizer.GetString("Scanner_Dev_Progress"));

        // 1. WSL2 VHDX Virtual Disks
        try
        {
            var packagesDir = Path.Combine(localAppData, "Packages");
            if (Directory.Exists(packagesDir))
            {
                foreach (var dir in Directory.EnumerateDirectories(packagesDir, "*CanonicalGroupLimited*").Concat(
                                     Directory.EnumerateDirectories(packagesDir, "*Debian*")).Concat(
                                     Directory.EnumerateDirectories(packagesDir, "*openSUSE*")).Concat(
                                     Directory.EnumerateDirectories(packagesDir, "*Kali*")).Concat(
                                     Directory.EnumerateDirectories(packagesDir, "*Alpine*")))
                {
                    ct.ThrowIfCancellationRequested();
                    var vhdx = Path.Combine(dir, "LocalState", "ext4.vhdx");
                    if (File.Exists(vhdx) && !IsProtectedScanTarget(vhdx))
                    {
                        var fi = new FileInfo(vhdx);
                        items.Add(new ScannedItem
                        {
                            Path = vhdx,
                            Size = fi.Length,
                            LastAccess = fi.LastWriteTime,
                            Description = Localizer.GetString("Scanner_Dev_Desc_WslVhdx", Path.GetFileName(dir)),
                            Explanation = Localizer.GetString("Scanner_Dev_Expl_WslPackages"),
                            HowToRestore = Localizer.GetString("Scanner_Dev_HowTo_Wsl"),
                            Risk = RiskCategory.PerformanceCache,
                            IsDirectory = false
                        });
                    }
                }
            }

            var directWslDir = DirectWslDirectory;
            if (Directory.Exists(directWslDir))
            {
                // Цикл-гвард: reparse-точки не рекурсируются; каждый item —
                // через гейт реального пути (findings M1, День 14).
                foreach (var vhdx in SafeEnumerateFilesRecursive(directWslDir, "ext4.vhdx"))
                {
                    ct.ThrowIfCancellationRequested();
                    if (IsProtectedScanTarget(vhdx)) continue;
                    var fi = new FileInfo(vhdx);
                    items.Add(new ScannedItem
                    {
                        Path = vhdx,
                        Size = fi.Length,
                        LastAccess = fi.LastWriteTime,
                        Description = Localizer.GetString("Scanner_Dev_Desc_WslVhdx", Path.GetFileName(Path.GetDirectoryName(vhdx)) ?? string.Empty),
                        Explanation = Localizer.GetString("Scanner_Dev_Expl_WslDirect"),
                        Risk = RiskCategory.PerformanceCache,
                        IsDirectory = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при сканировании WSL2");
        }

        // 2. Rust Cargo Caches
        var cargoCache = Path.Combine(userHome, ".cargo", "registry", "cache");
        AddDirectoryIfFound(items, cargoCache, "Rust Cargo Registry Cache", "Scanner_Dev_Desc_CargoCache", "Scanner_Dev_HowTo_CargoCache", RiskCategory.SafeToDelete);

        var cargoGitDb = Path.Combine(userHome, ".cargo", "git", "db");
        AddDirectoryIfFound(items, cargoGitDb, "Rust Cargo Git Database", "Scanner_Dev_Desc_CargoGitDb", "Scanner_Dev_HowTo_CargoGitDb", RiskCategory.SafeToDelete);

        // 3. Gradle Caches & Daemons
        var gradleCaches = Path.Combine(userHome, ".gradle", "caches");
        AddDirectoryIfFound(items, gradleCaches, "Gradle Build Caches", "Scanner_Dev_Desc_GradleCaches", "Scanner_Dev_HowTo_GradleCaches", RiskCategory.SafeToDelete);

        var gradleDaemon = Path.Combine(userHome, ".gradle", "daemon");
        AddDirectoryIfFound(items, gradleDaemon, "Gradle Daemon Logs", "Scanner_Dev_Desc_GradleDaemon", "Scanner_Dev_HowTo_GradleDaemon", RiskCategory.SafeToDelete);

        var gradleWrapper = Path.Combine(userHome, ".gradle", "wrapper", "dists");
        AddDirectoryIfFound(items, gradleWrapper, "Gradle Wrapper Dists", "Scanner_Dev_Desc_GradleWrapper", "Scanner_Dev_HowTo_GradleWrapper", RiskCategory.PerformanceCache);

        // 4. Maven Repository
        var m2Repo = Path.Combine(userHome, ".m2", "repository");
        AddDirectoryIfFound(items, m2Repo, "Maven Local Repository", "Scanner_Dev_Desc_M2Repo", "Scanner_Dev_HowTo_M2Repo", RiskCategory.PerformanceCache);

        // 5. Go Build & Module Caches
        var goBuild = Path.Combine(localAppData, "go-build");
        AddDirectoryIfFound(items, goBuild, "Go Build Cache", "Scanner_Dev_Desc_GoBuild", "Scanner_Dev_HowTo_GoBuild", RiskCategory.SafeToDelete);

        var goPkgMod = Path.Combine(userHome, "go", "pkg", "mod", "cache");
        AddDirectoryIfFound(items, goPkgMod, "Go Module Cache", "Scanner_Dev_Desc_GoPkgMod", "Scanner_Dev_HowTo_GoPkgMod", RiskCategory.SafeToDelete);

        // 6. Android SDK Temp & Snapshots
        var androidTemp = Path.Combine(localAppData, "Android", "Sdk", ".temp");
        AddDirectoryIfFound(items, androidTemp, "Android SDK Temp", "Scanner_Dev_Desc_AndroidTemp", "Scanner_Dev_HowTo_AndroidTemp", RiskCategory.SafeToDelete);

        var androidAvdSnapshots = AndroidAvdSnapshotsDirectory;
        if (Directory.Exists(androidAvdSnapshots))
        {
            try
            {
                // Цикл-гвард: reparse-точки не рекурсируются (findings M1, День 14) —
                // циклический junction под корнем AVD не рывает перечисление снапшотов.
                foreach (var snapDir in SafeEnumerateDirectoriesRecursive(androidAvdSnapshots, "snapshots"))
                {
                    AddDirectoryIfFound(items, snapDir, "Android Emulator Snapshot", "Scanner_Dev_Desc_AndroidSnapshot", "Scanner_Dev_HowTo_AndroidSnapshot", RiskCategory.PerformanceCache);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DevSuperScanner] Android snapshot enumeration error: {ex.Message}"); }
        }

        // 7. Dart / Flutter Pub Cache
        var pubCache = Path.Combine(localAppData, "Pub", "Cache");
        AddDirectoryIfFound(items, pubCache, "Flutter / Dart Pub Cache", "Scanner_Dev_Desc_PubCache", "Scanner_Dev_HowTo_PubCache", RiskCategory.PerformanceCache);

        // 8. Unity & Unreal Engine Caches
        var unityCache = Path.Combine(localAppData, "Unity", "cache");
        AddDirectoryIfFound(items, unityCache, "Unity Editor Cache", "Scanner_Dev_Desc_UnityCache", "Scanner_Dev_HowTo_UnityCache", RiskCategory.SafeToDelete);

        var unrealDdc = Path.Combine(localAppData, "UnrealEngine", "Common", "DerivedDataCache");
        AddDirectoryIfFound(items, unrealDdc, "Unreal Engine Derived Data Cache (DDC)", "Scanner_Dev_Desc_UnrealDdc", "Scanner_Dev_HowTo_UnrealDdc", RiskCategory.SafeToDelete);

        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };

        return Task.FromResult(result);
    }

    private void AddDirectoryIfFound(List<ScannedItem> items, string path, string name, string descKey, string howToRestoreKey, RiskCategory risk)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        if (IsProtectedScanTarget(path)) return; // гейт выдачи по реальному пути (findings M1)

        try
        {
            var size = CalculateDirectorySize(path);
            if (size > 0)
            {
                var desc = Localizer.GetString(descKey);
                items.Add(new ScannedItem
                {
                    Path = path,
                    Size = size,
                    LastAccess = Directory.GetLastWriteTime(path),
                    Description = $"{name}: {desc}",
                    Explanation = desc,
                    HowToRestore = Localizer.GetString(howToRestoreKey),
                    Risk = risk,
                    IsDirectory = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при сканировании директории {Path}", path);
        }
    }
}
