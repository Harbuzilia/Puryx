using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер игровых платформ и шейдерных кэшей
/// </summary>
public class GamesScanner : ScannerBase
{
    public override string CategoryName => "Игры";
    public override string CategoryIcon => "\uE7FC"; // Segoe MDL2: Game
    public override int DisplayOrder => 3;

    public GamesScanner(IKnowledgeBase knowledge, ISafetyService safety) 
        : base(knowledge, safety)
    {
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            // Steam
            progress?.Report("Сканирование Steam...");
            ScanSteam(items, ct);

            // Epic Games
            progress?.Report("Сканирование Epic Games...");
            ScanEpicGames(items, ct);

            // NVIDIA Shader Cache
            progress?.Report("Сканирование NVIDIA shader cache...");
            ScanNvidiaCache(items, ct);

            // AMD Shader Cache
            progress?.Report("Сканирование AMD shader cache...");
            ScanAmdCache(items, ct);

            // Intel Shader Cache
            progress?.Report("Сканирование Intel shader cache...");
            ScanIntelCache(items, ct);

            // Unity
            progress?.Report("Сканирование Unity...");
            ScanUnity(items, ct);

            // Unreal Engine
            progress?.Report("Сканирование Unreal Engine...");
            ScanUnreal(items, ct);

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    private void ScanSteam(List<ScannedItem> items, CancellationToken ct)
    {
        // Steam в Program Files (x86)
        var steamPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam");

        if (!Directory.Exists(steamPath)) return;

        try
        {
            // appcache
            var appCache = Path.Combine(steamPath, "appcache");
            if (Directory.Exists(appCache))
            {
                var size = CalculateDirectorySize(appCache);
                if (size > 1024 * 1024) // > 1 MB
                {
                    items.Add(CreateDirectoryItem(
                        appCache,
                        RiskCategory.PerformanceCache,
                        "Кэш приложений Steam. Замедлит загрузку магазина.",
                        "Steam"));
                }
            }

            // depotcache
            var depotCache = Path.Combine(steamPath, "depotcache");
            if (Directory.Exists(depotCache))
            {
                var size = CalculateDirectorySize(depotCache);
                if (size > 1024 * 1024)
                {
                    items.Add(CreateDirectoryItem(
                        depotCache,
                        RiskCategory.SafeToDelete,
                        "Кэш загрузок Steam",
                        "Steam"));
                }
            }

            // logs
            var logs = Path.Combine(steamPath, "logs");
            if (Directory.Exists(logs))
            {
                var size = CalculateDirectorySize(logs);
                if (size > 512 * 1024) // > 512 KB
                {
                    items.Add(CreateDirectoryItem(
                        logs,
                        RiskCategory.SafeToDelete,
                        "Логи Steam",
                        "Steam"));
                }
            }

            // dumps
            var dumps = Path.Combine(steamPath, "dumps");
            if (Directory.Exists(dumps))
            {
                var size = CalculateDirectorySize(dumps);
                if (size > 0)
                {
                    items.Add(CreateDirectoryItem(
                        dumps,
                        RiskCategory.SafeToDelete,
                        "Дампы Steam",
                        "Steam"));
                }
            }

            // Shader cache в appcache/shadercache
            var shaderCache = Path.Combine(appCache, "shadercache");
            if (Directory.Exists(shaderCache))
            {
                var size = CalculateDirectorySize(shaderCache);
                if (size > 10 * 1024 * 1024) // > 10 MB
                {
                    items.Add(CreateDirectoryItem(
                        shaderCache,
                        RiskCategory.PerformanceCache,
                        "Кэш шейдеров Steam. Первый запуск игр будет дольше.",
                        "Steam"));
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanEpicGames(List<ScannedItem> items, CancellationToken ct)
    {
        var epicPath = ExpandPath(@"%LOCALAPPDATA%\EpicGamesLauncher");
        if (!Directory.Exists(epicPath)) return;

        try
        {
            // Saved/webcache
            var webCache = Path.Combine(epicPath, "Saved", "webcache");
            if (Directory.Exists(webCache))
            {
                var size = CalculateDirectorySize(webCache);
                if (size > 1024 * 1024)
                {
                    items.Add(CreateDirectoryItem(
                        webCache,
                        RiskCategory.PerformanceCache,
                        "Веб-кэш Epic Games Launcher",
                        "Epic Games"));
                }
            }

            // Saved/Logs
            var logs = Path.Combine(epicPath, "Saved", "Logs");
            if (Directory.Exists(logs))
            {
                var size = CalculateDirectorySize(logs);
                if (size > 512 * 1024)
                {
                    items.Add(CreateDirectoryItem(
                        logs,
                        RiskCategory.SafeToDelete,
                        "Логи Epic Games Launcher",
                        "Epic Games"));
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        // VaultCache
        var vaultCache = ExpandPath(@"%PROGRAMDATA%\Epic\VaultCache");
        if (Directory.Exists(vaultCache))
        {
            try
            {
                var size = CalculateDirectorySize(vaultCache);
                if (size > 100 * 1024 * 1024) // > 100 MB
                {
                    items.Add(CreateDirectoryItem(
                        vaultCache,
                        RiskCategory.PerformanceCache,
                        "Кэш ассетов Unreal Marketplace. Может быть очень большим!",
                        "Epic Games"));
                }
            }
            catch { }
        }
    }

    private void ScanNvidiaCache(List<ScannedItem> items, CancellationToken ct)
    {
        var paths = new[]
        {
            ExpandPath(@"%LOCALAPPDATA%\NVIDIA\DXCache"),
            ExpandPath(@"%LOCALAPPDATA%\NVIDIA\GLCache"),
            ExpandPath(@"%LOCALAPPDATA%\NVIDIA Corporation\NV_Cache")
        };

        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;

            try
            {
                var size = CalculateDirectorySize(path);
                if (size > 10 * 1024 * 1024) // > 10 MB
                {
                    var name = Path.GetFileName(path);
                    items.Add(CreateDirectoryItem(
                        path,
                        RiskCategory.PerformanceCache,
                        $"Кэш шейдеров NVIDIA ({name}). После очистки игры могут подтормаживать.",
                        "NVIDIA"));
                }
            }
            catch { }
        }
    }

    private void ScanAmdCache(List<ScannedItem> items, CancellationToken ct)
    {
        var paths = new[]
        {
            ExpandPath(@"%LOCALAPPDATA%\AMD\DxCache"),
            ExpandPath(@"%LOCALAPPDATA%\AMD\GLCache"),
            ExpandPath(@"%LOCALAPPDATA%\AMD\VkCache")
        };

        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;

            try
            {
                var size = CalculateDirectorySize(path);
                if (size > 10 * 1024 * 1024)
                {
                    var name = Path.GetFileName(path);
                    items.Add(CreateDirectoryItem(
                        path,
                        RiskCategory.PerformanceCache,
                        $"Кэш шейдеров AMD ({name}). После очистки игры могут подтормаживать.",
                        "AMD"));
                }
            }
            catch { }
        }
    }

    private void ScanIntelCache(List<ScannedItem> items, CancellationToken ct)
    {
        var path = ExpandPath(@"%LOCALAPPDATA%\Intel\ShaderCache");
        if (!Directory.Exists(path)) return;

        try
        {
            var size = CalculateDirectorySize(path);
            if (size > 5 * 1024 * 1024) // > 5 MB
            {
                items.Add(CreateDirectoryItem(
                    path,
                    RiskCategory.PerformanceCache,
                    "Кэш шейдеров Intel",
                    "Intel"));
            }
        }
        catch { }
    }

    private void ScanUnity(List<ScannedItem> items, CancellationToken ct)
    {
        var unityPath = ExpandPath(@"%LOCALAPPDATA%\Unity");
        if (!Directory.Exists(unityPath)) return;

        try
        {
            // cache
            var cache = Path.Combine(unityPath, "cache");
            if (Directory.Exists(cache))
            {
                var size = CalculateDirectorySize(cache);
                if (size > 50 * 1024 * 1024) // > 50 MB
                {
                    items.Add(CreateDirectoryItem(
                        cache,
                        RiskCategory.PerformanceCache,
                        "Кэш Unity Hub/Editor",
                        "Unity"));
                }
            }
        }
        catch { }
    }

    private void ScanUnreal(List<ScannedItem> items, CancellationToken ct)
    {
        var unrealPath = ExpandPath(@"%LOCALAPPDATA%\UnrealEngine");
        if (!Directory.Exists(unrealPath)) return;

        try
        {
            foreach (var versionDir in Directory.EnumerateDirectories(unrealPath))
            {
                ct.ThrowIfCancellationRequested();

                // DerivedDataCache — может быть ОГРОМНЫМ
                var ddc = Path.Combine(versionDir, "DerivedDataCache");
                if (Directory.Exists(ddc))
                {
                    var size = CalculateDirectorySize(ddc);
                    if (size > 100 * 1024 * 1024) // > 100 MB
                    {
                        var version = Path.GetFileName(versionDir);
                        items.Add(CreateDirectoryItem(
                            ddc,
                            RiskCategory.PerformanceCache,
                            $"DerivedDataCache UE {version}. Может занимать гигабайты!",
                            "Unreal Engine"));
                    }
                }

                // Saved/Logs
                var logs = Path.Combine(versionDir, "Saved", "Logs");
                if (Directory.Exists(logs))
                {
                    var size = CalculateDirectorySize(logs);
                    if (size > 1024 * 1024)
                    {
                        items.Add(CreateDirectoryItem(
                            logs,
                            RiskCategory.SafeToDelete,
                            "Логи Unreal Engine",
                            "Unreal Engine"));
                    }
                }

                // Saved/Crashes
                var crashes = Path.Combine(versionDir, "Saved", "Crashes");
                if (Directory.Exists(crashes))
                {
                    var size = CalculateDirectorySize(crashes);
                    if (size > 0)
                    {
                        items.Add(CreateDirectoryItem(
                            crashes,
                            RiskCategory.SafeToDelete,
                            "Отчёты о сбоях UE",
                            "Unreal Engine"));
                    }
                }
            }
        }
        catch { }
    }
}
