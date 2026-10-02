using Microsoft.Extensions.Logging;
using SmartCleaner.Core.Helpers;
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

    public override Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        progress?.Report("Сканирование виртуальных дисков WSL2 и кэшей сборщика...");

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
                    if (File.Exists(vhdx))
                    {
                        var fi = new FileInfo(vhdx);
                        items.Add(new ScannedItem
                        {
                            Path = vhdx,
                            Size = fi.Length,
                            LastAccess = fi.LastWriteTime,
                            Description = $"Виртуальный диск Linux WSL2 ({Path.GetFileName(dir)})",
                            Explanation = "Файл образа диска WSL2. Занимает место на диске даже после удаления файлов внутри Linux. Подлежит сжатию.",
                            HowToRestore = "Используйте встроенную функцию сжатия WSL для высвобождения неиспользуемых блоков без потери данных.",
                            Risk = RiskCategory.PerformanceCache,
                            IsDirectory = false
                        });
                    }
                }
            }

            var directWslDir = Path.Combine(localAppData, "wsl");
            if (Directory.Exists(directWslDir))
            {
                foreach (var vhdx in Directory.EnumerateFiles(directWslDir, "ext4.vhdx", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(vhdx);
                    items.Add(new ScannedItem
                    {
                        Path = vhdx,
                        Size = fi.Length,
                        LastAccess = fi.LastWriteTime,
                        Description = $"Виртуальный диск Linux WSL2 ({Path.GetFileName(Path.GetDirectoryName(vhdx))})",
                        Explanation = "Образ диска дистрибутива WSL2.",
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
        AddDirectoryIfFound(items, cargoCache, "Rust Cargo Registry Cache", "Кэшированные .crate архивы пакетов Rust", "Скачиваются автоматически командой cargo build", RiskCategory.SafeToDelete);

        var cargoGitDb = Path.Combine(userHome, ".cargo", "git", "db");
        AddDirectoryIfFound(items, cargoGitDb, "Rust Cargo Git Database", "Git-репозитории зависимостей Cargo", "Склонируются заново при следующей сборке", RiskCategory.SafeToDelete);

        // 3. Gradle Caches & Daemons
        var gradleCaches = Path.Combine(userHome, ".gradle", "caches");
        AddDirectoryIfFound(items, gradleCaches, "Gradle Build Caches", "Кэшированные зависимости и артефакты сборки Gradle", "Загружаются заново при запуске сборки gradle", RiskCategory.SafeToDelete);

        var gradleDaemon = Path.Combine(userHome, ".gradle", "daemon");
        AddDirectoryIfFound(items, gradleDaemon, "Gradle Daemon Logs", "Логи и временные файлы фоновых процессов Gradle", "Не влияют на сборку", RiskCategory.SafeToDelete);

        var gradleWrapper = Path.Combine(userHome, ".gradle", "wrapper", "dists");
        AddDirectoryIfFound(items, gradleWrapper, "Gradle Wrapper Dists", "Старые распакованные дистрибутивы Gradle Wrapper", "Wrapper скачает нужную версию при необходимости", RiskCategory.PerformanceCache);

        // 4. Maven Repository
        var m2Repo = Path.Combine(userHome, ".m2", "repository");
        AddDirectoryIfFound(items, m2Repo, "Maven Local Repository", "Локальный кэш Java зависимостей Maven (.m2)", "Maven загрузит зависимости из репозиториев при сборке", RiskCategory.PerformanceCache);

        // 5. Go Build & Module Caches
        var goBuild = Path.Combine(localAppData, "go-build");
        AddDirectoryIfFound(items, goBuild, "Go Build Cache", "Кэш инкрементальной сборки компилятора Go", "Создается автоматически", RiskCategory.SafeToDelete);

        var goPkgMod = Path.Combine(userHome, "go", "pkg", "mod", "cache");
        AddDirectoryIfFound(items, goPkgMod, "Go Module Cache", "Кэшированные исходные коды модулей Go", "Загружаются командой go mod download", RiskCategory.SafeToDelete);

        // 6. Android SDK Temp & Snapshots
        var androidTemp = Path.Combine(localAppData, "Android", "Sdk", ".temp");
        AddDirectoryIfFound(items, androidTemp, "Android SDK Temp", "Временные файлы загрузки компонентов Android SDK", "Удаление безопасно", RiskCategory.SafeToDelete);

        var androidAvdSnapshots = Path.Combine(userHome, ".android", "avd");
        if (Directory.Exists(androidAvdSnapshots))
        {
            try
            {
                foreach (var snapDir in Directory.EnumerateDirectories(androidAvdSnapshots, "snapshots", SearchOption.AllDirectories))
                {
                    AddDirectoryIfFound(items, snapDir, "Android Emulator Snapshot", "Снимки состояния эмуляторов Android", "Снимок пересоздастся при закрытии эмулятора", RiskCategory.PerformanceCache);
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DevSuperScanner] Android snapshot enumeration error: {ex.Message}"); }
        }

        // 7. Dart / Flutter Pub Cache
        var pubCache = Path.Combine(localAppData, "Pub", "Cache");
        AddDirectoryIfFound(items, pubCache, "Flutter / Dart Pub Cache", "Кэш пакетов Flutter и Dart", "Пакеты загружаются командой flutter pub get", RiskCategory.PerformanceCache);

        // 8. Unity & Unreal Engine Caches
        var unityCache = Path.Combine(localAppData, "Unity", "cache");
        AddDirectoryIfFound(items, unityCache, "Unity Editor Cache", "Кэши ассетов и шейдеров Unity", "Пересоздаются редактором Unity", RiskCategory.SafeToDelete);

        var unrealDdc = Path.Combine(localAppData, "UnrealEngine", "Common", "DerivedDataCache");
        AddDirectoryIfFound(items, unrealDdc, "Unreal Engine Derived Data Cache (DDC)", "Скомпилированные ассеты и шейдеры Unreal Engine", "Перегенерируются движком", RiskCategory.SafeToDelete);

        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };

        return Task.FromResult(result);
    }

    private void AddDirectoryIfFound(List<ScannedItem> items, string path, string name, string desc, string howToRestore, RiskCategory risk)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        try
        {
            var size = CalculateDirectorySize(path);
            if (size > 0)
            {
                items.Add(new ScannedItem
                {
                    Path = path,
                    Size = size,
                    LastAccess = Directory.GetLastWriteTime(path),
                    Description = $"{name}: {desc}",
                    Explanation = desc,
                    HowToRestore = howToRestore,
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
