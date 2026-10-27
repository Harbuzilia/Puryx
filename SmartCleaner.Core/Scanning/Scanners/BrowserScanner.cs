using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

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

    private readonly IConfigService? _configService;
    private readonly string? _localAppDataRoot;
    private readonly string? _roamingAppDataRoot;

    /// <summary>
    /// DI-конструктор. <paramref name="configService"/> — единая настройка
    /// сканирования (<see cref="ScanSettingsConfig"/>, cookie opt-in); null — дефолты.
    /// <paramref name="localAppDataRoot"/>/<paramref name="roamingAppDataRoot"/> —
    /// тест-шов корней %LOCALAPPDATA%/%APPDATA% (null — окружение процесса).
    /// </summary>
    public BrowserScanner(
        ISafetyService safety,
        IConfigService? configService = null,
        string? localAppDataRoot = null,
        string? roamingAppDataRoot = null)
        : base(safety)
    {
        _configService = configService;
        _localAppDataRoot = localAppDataRoot;
        _roamingAppDataRoot = roamingAppDataRoot;
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
                progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", name));
                ScanChromiumBrowser(items, name, pathTemplate, ct);
            }

            // Firefox
            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Firefox"));
            ScanFirefox(items, ct);

            // Cookie-хранилища — только при явном opt-in (ROADMAP v3.1, День 14)
            if (IsCookieOptInEnabled())
            {
                progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Cookies"));
                ScanBrowserCookies(items, ct);
            }

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
        var basePath = ExpandBrowserPath(pathTemplate);
        if (!Directory.Exists(basePath)) return;

        try
        {
            foreach (var profilePath in EnumerateChromiumProfiles(browserName, basePath))
            {
                ct.ThrowIfCancellationRequested();

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
                                "Cache" => Localizer.GetString("Scanner_Browser_Desc_PagesCache"),
                                "Code Cache" => Localizer.GetString("Scanner_Desc_JavaScriptCache"),
                                "GPUCache" => Localizer.GetString("Scanner_Desc_GpuCache"),
                                _ when cacheFolder.Contains("Service Worker") => Localizer.GetString("Scanner_Browser_Desc_ServiceWorkerCache"),
                                _ when cacheFolder.Contains("Shader") => Localizer.GetString("Scanner_Desc_ShaderCache"),
                                _ => Localizer.GetString("Scanner_Browser_Desc_BrowserCache")
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
                            Localizer.GetString("Scanner_Desc_ShaderCache"),
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
        var profilesPath = ExpandBrowserPath(@"%APPDATA%\Mozilla\Firefox\Profiles");
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
                            Localizer.GetString("Scanner_Browser_Desc_FirefoxPagesCache"),
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
                            Localizer.GetString("Scanner_Browser_Desc_FirefoxShaderCache"),
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
                            Localizer.GetString("Scanner_Browser_Desc_FirefoxStartupCache"),
                            "Firefox"));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Cookie-хранилища браузеров — категория <see cref="RiskCategory.UserData"/>
    /// с явным opt-in (ROADMAP v3.1, День 14, фаза B).
    /// Chromium: <c>Network\Cookies</c> в профиле, фолбэк — legacy <c>Cookies</c>;
    /// Firefox: <c>cookies.sqlite</c> в каждом профиле. Элементы всегда
    /// <c>IsSelected = false</c> (только ручной выбор) и помечены
    /// <see cref="ScannedItem.IsUserDataOptIn"/> — удаление требует подтверждения.
    /// Защита путей — обычная: whitelist-гейт по реальному пути (<see cref="ScannerBase.IsProtectedScanTarget"/>).
    /// </summary>
    private void ScanBrowserCookies(List<ScannedItem> items, CancellationToken ct)
    {
        foreach (var (name, pathTemplate) in ChromiumBrowsers)
        {
            ct.ThrowIfCancellationRequested();

            var basePath = ExpandBrowserPath(pathTemplate);
            if (!Directory.Exists(basePath)) continue;

            try
            {
                foreach (var profilePath in EnumerateChromiumProfiles(name, basePath))
                {
                    ct.ThrowIfCancellationRequested();

                    // Актуальный путь Chromium; legacy Cookies — только при его отсутствии
                    var networkCookies = Path.Combine(profilePath, "Network", "Cookies");
                    if (File.Exists(networkCookies))
                    {
                        AddCookieItem(items, name, networkCookies);
                        continue;
                    }

                    var legacyCookies = Path.Combine(profilePath, "Cookies");
                    if (File.Exists(legacyCookies))
                    {
                        AddCookieItem(items, name, legacyCookies);
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        var firefoxProfiles = ExpandBrowserPath(@"%APPDATA%\Mozilla\Firefox\Profiles");
        if (!Directory.Exists(firefoxProfiles)) return;

        try
        {
            foreach (var profilePath in Directory.EnumerateDirectories(firefoxProfiles))
            {
                ct.ThrowIfCancellationRequested();

                var cookiesPath = Path.Combine(profilePath, "cookies.sqlite");
                if (File.Exists(cookiesPath))
                {
                    AddCookieItem(items, "Firefox", cookiesPath);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Элемент cookie-хранилища: UserData + opt-in-флаг, никогда не выбран
    /// автоматически. Пустой/нечитаемый файл и путь под whitelist'ом не выдаются.
    /// </summary>
    private void AddCookieItem(List<ScannedItem> items, string browserName, string cookiePath)
    {
        if (IsProtectedScanTarget(cookiePath)) return;

        var size = GetFileSize(cookiePath);
        if (size <= 0) return;

        items.Add(new ScannedItem
        {
            Path = cookiePath,
            Size = size,
            LastAccess = GetLastAccess(cookiePath),
            Risk = RiskCategory.UserData,
            IsUserDataOptIn = true,
            Description = Localizer.GetString("Browser_Cookies_Desc", browserName),
            Explanation = Localizer.GetString("Browser_Cookies_Explanation"),
            HowToRestore = Localizer.GetString("Browser_Cookies_HowToRestore"),
            IsDirectory = false,
            ParentApp = browserName,
            IsSelected = false, // opt-in: только ручной выбор
            IsLocked = IsBrowserRunning(browserName)
        });
    }

    /// <summary>
    /// Профили Chromium-браузера: Opera хранит профиль прямо в корне (Fix #10),
    /// остальные — <c>Default</c> + <c>Profile N</c>. Возвращаются только
    /// существующие каталоги; ошибки доступа — пустой/частичный список.
    /// </summary>
    private static List<string> EnumerateChromiumProfiles(string browserName, string basePath)
    {
        if (browserName.Equals("Opera", StringComparison.OrdinalIgnoreCase))
        {
            return [basePath];
        }

        var profiles = new List<string>();
        var defaultProfile = Path.Combine(basePath, "Default");
        if (Directory.Exists(defaultProfile))
        {
            profiles.Add(defaultProfile);
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(basePath))
            {
                if (Path.GetFileName(dir).StartsWith("Profile ", StringComparison.Ordinal))
                {
                    profiles.Add(dir);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return profiles;
    }

    /// <summary>Opt-in cookie-категории из единой настройки сканирования.</summary>
    private bool IsCookieOptInEnabled()
    {
        try
        {
            return _configService?.Load<ScanSettingsConfig>(ScanSettingsConfig.Filename).BrowserCleanCookies ?? false;
        }
        catch
        {
            return false; // битая настройка — безопасный дефолт (opt-in выключен)
        }
    }

    /// <summary>
    /// Развернуть путь-шаблон браузера. Тест-шов: при заданных корнях
    /// %LOCALAPPDATA%/%APPDATA% подменяются ими, иначе — окружение процесса
    /// (поведение прежних версий без изменений).
    /// </summary>
    private string ExpandBrowserPath(string template)
    {
        if (_localAppDataRoot is null && _roamingAppDataRoot is null)
        {
            return ExpandPath(template);
        }

        var localAppData = _localAppDataRoot ?? ExpandPath("%LOCALAPPDATA%");
        var roamingAppData = _roamingAppDataRoot ?? ExpandPath("%APPDATA%");

        return template
            .Replace("%LOCALAPPDATA%", localAppData, StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", roamingAppData, StringComparison.OrdinalIgnoreCase);
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
