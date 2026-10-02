using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер кэша браузеров
/// </summary>
public class BrowserScanner : ScannerBase
{
    public override string CategoryName => "Браузеры";
    public override string CategoryIcon => "\uE774"; // Segoe MDL2: Globe
    public override int DisplayOrder => 2;

    // Chromium-based browsers
    private static readonly Dictionary<string, string> ChromiumBrowsers = new()
    {
        ["Chrome"] = @"%LOCALAPPDATA%\Google\Chrome\User Data",
        ["Edge"] = @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
        ["Brave"] = @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data",
        ["Opera"] = @"%APPDATA%\Opera Software\Opera Stable",
        ["Vivaldi"] = @"%LOCALAPPDATA%\Vivaldi\User Data",
        ["Yandex"] = @"%LOCALAPPDATA%\Yandex\YandexBrowser\User Data"
    };

    // Папки кэша для Chromium
    private static readonly string[] ChromiumCacheFolders =
    [
        "Cache",
        "Code Cache",
        "GPUCache",
        "Service Worker/CacheStorage",
        "Service Worker/ScriptCache",
        "ShaderCache",
        "GrShaderCache"
    ];

    /// <summary>Кэш имён запущенных процессов — заполняется один раз в ScanAsync</summary>
    private HashSet<string> _runningProcessNames = new(StringComparer.OrdinalIgnoreCase);

    public BrowserScanner(ISafetyService safety) 
        : base(safety)
    {
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            // Fix #7: кэшируем список процессов один раз
            try
            {
                _runningProcessNames = new HashSet<string>(
                    System.Diagnostics.Process.GetProcesses().Select(p => p.ProcessName),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[BrowserScanner] Process enumeration error: {ex.Message}"); _runningProcessNames = new(StringComparer.OrdinalIgnoreCase); }

            // Chromium браузеры
            foreach (var (name, pathTemplate) in ChromiumBrowsers)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Сканирование {name}...");
                ScanChromiumBrowser(items, name, pathTemplate, ct);
            }

            // Firefox
            progress?.Report("Сканирование Firefox...");
            ScanFirefox(items, ct);

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    private void ScanChromiumBrowser(List<ScannedItem> items, string browserName, string pathTemplate, CancellationToken ct)
    {
        var basePath = ExpandPath(pathTemplate);
        if (!Directory.Exists(basePath)) return;

        try
        {
            // Получаем все профили (Default, Profile 1, Profile 2, ...)
            // Fix #10: Opera не использует подпапку Default — кэш в корне
            var isOpera = browserName.Equals("Opera", StringComparison.OrdinalIgnoreCase);
            var profiles = isOpera 
                ? new List<string> { "" } // Opera: cache прямо в basePath
                : new List<string> { "Default" };

            if (!isOpera)
            {
                foreach (var dir in Directory.EnumerateDirectories(basePath))
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName.StartsWith("Profile "))
                    {
                        profiles.Add(dirName);
                    }
                }
            }

            foreach (var profile in profiles)
            {
                ct.ThrowIfCancellationRequested();
                
                var profilePath = isOpera ? basePath : Path.Combine(basePath, profile);
                if (!Directory.Exists(profilePath)) continue;

                foreach (var cacheFolder in ChromiumCacheFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var cachePath = Path.Combine(profilePath, cacheFolder);
                    if (Directory.Exists(cachePath))
                    {
                        var size = CalculateDirectorySize(cachePath);
                        if (size > 1024 * 100) // > 100 KB
                        {
                            var description = cacheFolder switch
                            {
                                "Cache" => "Кэш страниц и изображений",
                                "Code Cache" => "Кэш JavaScript кода",
                                "GPUCache" => "Кэш GPU",
                                _ when cacheFolder.Contains("Service Worker") => "Кэш Service Worker",
                                _ when cacheFolder.Contains("Shader") => "Кэш шейдеров",
                                _ => "Кэш браузера"
                            };

                            items.Add(new ScannedItem
                            {
                                Path = cachePath,
                                Size = size,
                                LastAccess = GetLastAccess(cachePath),
                                Risk = RiskCategory.PerformanceCache,
                                Description = description,
                                IsDirectory = true,
                                ParentApp = browserName,
                                IsSelected = true,
                                IsLocked = IsBrowserRunning(browserName)
                            });
                        }
                    }
                }
            }

            // Общие папки кэша (не в профиле)
            foreach (var cacheName in new[] { "ShaderCache", "GrShaderCache" })
            {
                var cachePath = Path.Combine(basePath, cacheName);
                if (Directory.Exists(cachePath))
                {
                    var size = CalculateDirectorySize(cachePath);
                    if (size > 1024 * 100)
                    {
                        items.Add(CreateDirectoryItem(
                            cachePath, 
                            RiskCategory.PerformanceCache, 
                            "Кэш шейдеров",
                            browserName));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanFirefox(List<ScannedItem> items, CancellationToken ct)
    {
        var profilesPath = ExpandPath(@"%APPDATA%\Mozilla\Firefox\Profiles");
        if (!Directory.Exists(profilesPath)) return;

        try
        {
            foreach (var profileDir in Directory.EnumerateDirectories(profilesPath))
            {
                ct.ThrowIfCancellationRequested();

                // cache2
                var cache2Path = Path.Combine(profileDir, "cache2");
                if (Directory.Exists(cache2Path))
                {
                    var size = CalculateDirectorySize(cache2Path);
                    if (size > 1024 * 100)
                    {
                        items.Add(CreateDirectoryItem(
                            cache2Path,
                            RiskCategory.PerformanceCache,
                            "Кэш страниц Firefox",
                            "Firefox"));
                    }
                }

                // shader-cache
                var shaderPath = Path.Combine(profileDir, "shader-cache");
                if (Directory.Exists(shaderPath))
                {
                    var size = CalculateDirectorySize(shaderPath);
                    if (size > 1024 * 100)
                    {
                        items.Add(CreateDirectoryItem(
                            shaderPath,
                            RiskCategory.PerformanceCache,
                            "Кэш шейдеров Firefox",
                            "Firefox"));
                    }
                }

                // startupCache
                var startupCachePath = Path.Combine(profileDir, "startupCache");
                if (Directory.Exists(startupCachePath))
                {
                    var size = CalculateDirectorySize(startupCachePath);
                    if (size > 1024 * 100)
                    {
                        items.Add(CreateDirectoryItem(
                            startupCachePath,
                            RiskCategory.PerformanceCache,
                            "Кэш запуска Firefox",
                            "Firefox"));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Проверить, запущен ли браузер (использует кэш процессов)
    /// </summary>
    private bool IsBrowserRunning(string browserName)
    {
        var processNames = browserName.ToLowerInvariant() switch
        {
            "chrome" => new[] { "chrome" },
            "edge" => new[] { "msedge" },
            "brave" => new[] { "brave" },
            "opera" => new[] { "opera" },
            "firefox" => new[] { "firefox" },
            "vivaldi" => new[] { "vivaldi" },
            "yandex" => new[] { "browser" },
            _ => Array.Empty<string>()
        };

        return processNames.Any(name => _runningProcessNames.Contains(name));
    }
}
