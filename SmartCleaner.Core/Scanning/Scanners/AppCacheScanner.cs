using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер кэшей десктопных приложений — мессенджеры, IDE, медиа, офис.
/// Аналог секции "Applications" в CCleaner.
/// </summary>
public class AppCacheScanner : ScannerBase
{
    public override string CategoryName => "Приложения";
    public override string CategoryIcon => "\uE74C"; // Segoe MDL2: AllApps
    public override int DisplayOrder => 3;

    public AppCacheScanner(ISafetyService safety) 
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // ═══════════════ МЕССЕНДЖЕРЫ ═══════════════

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Discord"));
            ScanElectronApp(items, appData, "discord", "Discord",
                "Scanner_AppCache_Expl_Discord",
                "Scanner_AppCache_HowTo_Discord");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Telegram"));
            ScanDir(items, Path.Combine(appData, "Telegram Desktop", "tdata", "user_data", "cache"),
                "Telegram", "Scanner_AppCache_Expl_TelegramMedia",
                "Scanner_AppCache_HowTo_TelegramMedia");
            ScanDir(items, Path.Combine(appData, "Telegram Desktop", "tdata", "user_data", "media_cache"),
                "Telegram", "Scanner_AppCache_Expl_TelegramMediaCache",
                "Scanner_AppCache_HowTo_Auto");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "WhatsApp"));
            ScanElectronApp(items, localAppData, "WhatsApp", "WhatsApp",
                "Scanner_AppCache_Expl_WhatsApp",
                "Scanner_AppCache_HowTo_WhatsApp");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Slack"));
            ScanElectronApp(items, appData, "Slack", "Slack",
                "Scanner_AppCache_Expl_Slack",
                "Scanner_AppCache_HowTo_Slack");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Teams"));
            ScanElectronApp(items, appData, @"Microsoft\Teams", "Microsoft Teams",
                "Scanner_AppCache_Expl_Teams",
                "Scanner_AppCache_HowTo_Chats");
            // Новый Teams
            ScanElectronApp(items, localAppData, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache", "Teams (new)",
                "Scanner_AppCache_Expl_TeamsNew",
                "Scanner_AppCache_HowTo_Auto");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Zoom"));
            ScanDir(items, Path.Combine(appData, "Zoom", "data"),
                "Zoom", "Scanner_AppCache_Expl_Zoom",
                "Scanner_AppCache_HowTo_Zoom");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Skype"));
            ScanElectronApp(items, appData, @"Microsoft\Skype for Desktop", "Skype",
                "Scanner_AppCache_Expl_Skype",
                "Scanner_AppCache_HowTo_Chats");

            // ═══════════════ МЕДИА ═══════════════

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Spotify"));
            ScanDir(items, Path.Combine(localAppData, "Spotify", "Storage"),
                "Spotify", "Scanner_AppCache_Expl_SpotifyStorage",
                "Scanner_AppCache_HowTo_SpotifyStorage");
            ScanDir(items, Path.Combine(localAppData, "Spotify", "Data"),
                "Spotify", "Scanner_AppCache_Expl_SpotifyData",
                "Scanner_AppCache_HowTo_Auto");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Steam"));
            ScanDir(items, Path.Combine(localAppData, "Steam", "htmlcache"),
                "Steam", "Scanner_AppCache_Expl_SteamHtml",
                "Scanner_AppCache_HowTo_SteamHtml");
            // Steam shader cache (в каждой игре)
            var steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
                "Steam", "steamapps", "shadercache");
            ScanDir(items, steamPath,
                "Steam", "Scanner_AppCache_Expl_SteamShaders",
                "Scanner_AppCache_HowTo_SteamShaders");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Epic Games"));
            ScanDir(items, Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache"),
                "Epic Games", "Scanner_AppCache_Expl_EpicWebcache",
                "Scanner_AppCache_HowTo_EpicWebcache");
            ScanDir(items, Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Logs"),
                "Epic Games", "Scanner_AppCache_HowTo_EpicLogs");

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Adobe"));
            ScanAdobeCaches(items, appData, localAppData);

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "VLC"));
            ScanDir(items, Path.Combine(appData, "vlc", "art", "artistalbum"),
                "VLC", "Scanner_AppCache_Expl_VlcArt",
                "Scanner_AppCache_HowTo_VlcArt");

            // ═══════════════ IDE / РЕДАКТОРЫ ═══════════════

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "JetBrains IDE"));
            ScanJetBrainsCaches(items, localAppData);

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Visual Studio"));
            ScanVisualStudioCaches(items, localAppData);

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Android Studio"));
            var androidCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "cache");
            ScanDir(items, androidCache,
                "Android Studio", "Scanner_AppCache_HowTo_NextBuild");

            // ═══════════════ ОФИС ═══════════════

            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Microsoft Office"));
            ScanOfficeCaches(items, localAppData);

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    /// <summary>
    /// Сканирует стандартные кэш-папки Electron-приложения (Cache, Code Cache, GPUCache, logs).
    /// </summary>
    /// <param name="items">Список результатов</param>
    /// <param name="basePath">Базовый путь (AppData или LocalAppData)</param>
    /// <param name="appFolder">Подпапка приложения</param>
    /// <param name="appName">Отображаемое имя</param>
    /// <param name="explanationKey">Ключ описания кэша</param>
    /// <param name="howToRestoreKey">Ключ инструкции восстановления</param>
    private void ScanElectronApp(List<ScannedItem> items, string basePath, string appFolder,
        string appName, string explanationKey, string howToRestoreKey)
    {
        var appPath = Path.Combine(basePath, appFolder);
        if (!Directory.Exists(appPath)) return;

        string[] cacheFolders = ["Cache", "CachedData", "Code Cache", "GPUCache", 
            "GrShaderCache", "ShaderCache", "logs", "blob_storage"];

        foreach (var folder in cacheFolders)
        {
            var fullPath = Path.Combine(appPath, folder);
            if (!Directory.Exists(fullPath)) continue;

            var size = CalculateDirectorySize(fullPath);
            if (size < 512 * 1024) continue; // < 512 KB — не стоит показывать

            items.Add(new ScannedItem
            {
                Path = fullPath,
                Size = size,
                LastAccess = GetLastAccess(fullPath),
                Risk = RiskCategory.SafeToDelete,
                Description = $"{appName}: {folder}",
                Explanation = Localizer.GetString(explanationKey),
                HowToRestore = Localizer.GetString(howToRestoreKey),
                IsDirectory = true,
                ParentApp = appName,
                IsSelected = true
            });
        }
    }

    /// <summary>
    /// Сканирует одну конкретную директорию и добавляет если существует и достаточно большая.
    /// </summary>
    private void ScanDir(List<ScannedItem> items, string path, string appName,
        string explanationKey, string? howToRestoreKey = null, 
        long minSize = 1024 * 1024)
    {
        if (!Directory.Exists(path)) return;

        var size = CalculateDirectorySize(path);
        if (size < minSize) return;

        items.Add(new ScannedItem
        {
            Path = path,
            Size = size,
            LastAccess = GetLastAccess(path),
            Risk = RiskCategory.SafeToDelete,
            Description = $"{appName}: {Path.GetFileName(path)}",
            Explanation = Localizer.GetString(explanationKey),
            HowToRestore = howToRestoreKey is not null
                ? Localizer.GetString(howToRestoreKey)
                : Localizer.GetString("Scanner_AppCache_HowTo_Default"),
            IsDirectory = true,
            ParentApp = appName,
            IsSelected = true
        });
    }

    /// <summary>
    /// Сканирует кэши всех продуктов Adobe (Photoshop, Premiere, After Effects и др.)
    /// </summary>
    private void ScanAdobeCaches(List<ScannedItem> items, string appData, string localAppData)
    {
        // Adobe хранит кэши в нескольких местах
        string[] adobePaths = [
            Path.Combine(appData, "Adobe"),
            Path.Combine(localAppData, "Adobe")
        ];

        foreach (var adobeRoot in adobePaths)
        {
            if (!Directory.Exists(adobeRoot)) continue;

            foreach (var productDir in SafeEnumerateDirectories(adobeRoot))
            {
                var productName = Path.GetFileName(productDir);

                // Ищем типичные кэш-папки внутри каждого продукта
                string[] cacheNames = ["Cache", "CameraCacheV2", "CameraRaw", 
                    "Logs", "FTTemp", "Media Cache", "Media Cache Files",
                    "Peak Files", "GPU Snoop", "AAMUpdater"];

                foreach (var cacheName in cacheNames)
                {
                    var cachePath = Path.Combine(productDir, cacheName);
                    ScanDir(items, cachePath, $"Adobe {productName}",
                        "Scanner_AppCache_Expl_Adobe",
                        minSize: 512 * 1024);
                }
            }
        }
    }

    /// <summary>
    /// Сканирует кэши JetBrains IDE (IntelliJ, PyCharm, WebStorm, Rider, GoLand и др.)
    /// </summary>
    private void ScanJetBrainsCaches(List<ScannedItem> items, string localAppData)
    {
        var jetbrainsRoot = Path.Combine(localAppData, "JetBrains");
        if (!Directory.Exists(jetbrainsRoot)) return;

        foreach (var ideDir in SafeEnumerateDirectories(jetbrainsRoot))
        {
            var ideName = Path.GetFileName(ideDir);

            // caches, log, tmp — безопасны для удаления
            string[] safeToDelete = ["caches", "log", "tmp", "index", "LOCAL_HISTORY"];

            foreach (var folder in safeToDelete)
            {
                var path = Path.Combine(ideDir, folder);
                ScanDir(items, path, $"JetBrains {ideName}",
                    "Scanner_AppCache_Expl_JetBrains",
                    "Scanner_AppCache_HowTo_JetBrains",
                    minSize: 5 * 1024 * 1024); // JetBrains кэши > 5MB
            }
        }
    }

    /// <summary>
    /// Сканирует кэши Visual Studio (не VS Code — тот в AIAgentsScanner / BrowserScanner)
    /// </summary>
    private void ScanVisualStudioCaches(List<ScannedItem> items, string localAppData)
    {
        var vsRoot = Path.Combine(localAppData, "Microsoft", "VisualStudio");
        if (!Directory.Exists(vsRoot)) return;

        foreach (var versionDir in SafeEnumerateDirectories(vsRoot))
        {
            var version = Path.GetFileName(versionDir);

            string[] cacheFolders = ["ComponentModelCache", "ProjectAssemblies",
                "Extensions", "DesignTimeBuild"];

            foreach (var folder in cacheFolders)
            {
                var path = Path.Combine(versionDir, folder);
                ScanDir(items, path, $"Visual Studio {version}",
                    "Scanner_AppCache_Expl_VS",
                    minSize: 2 * 1024 * 1024);
            }
        }

        // MEF ComponentModelCache в Exp
        var mefCache = Path.Combine(vsRoot, "MeowComponent");
        ScanDir(items, mefCache, "Visual Studio",
            "Scanner_AppCache_Expl_MEF");
    }

    /// <summary>
    /// Сканирует кэши Microsoft Office (Word, Excel, PowerPoint, Outlook)
    /// </summary>
    private void ScanOfficeCaches(List<ScannedItem> items, string localAppData)
    {
        // Office 365 / Office 2019+
        var officeRoot = Path.Combine(localAppData, "Microsoft", "Office");
        if (Directory.Exists(officeRoot))
        {
            foreach (var versionDir in SafeEnumerateDirectories(officeRoot))
            {
                string[] cacheFolders = ["OfficeFileCache", "Spw", "TokenBroker"];
                foreach (var folder in cacheFolders)
                {
                    var path = Path.Combine(versionDir, folder);
                    ScanDir(items, path, "Microsoft Office",
                        "Scanner_AppCache_Expl_Office",
                        minSize: 512 * 1024);
                }
            }
        }

        // Outlook кэш
        var outlookCache = Path.Combine(localAppData, "Microsoft", "Outlook", "RoamCache");
        ScanDir(items, outlookCache, "Outlook",
            "Scanner_AppCache_Expl_Outlook");

        // OneNote кэш
        var onenoteCache = Path.Combine(localAppData, "Microsoft", "OneNote");
        if (Directory.Exists(onenoteCache))
        {
            foreach (var dir in SafeEnumerateDirectories(onenoteCache))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("cache", StringComparison.OrdinalIgnoreCase))
                {
                    ScanDir(items, dir, "OneNote",
                        "Scanner_AppCache_Expl_OneNote");
                }
            }
        }

        // Recent файлы Office
        var recentDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Office", "Recent");
        ScanDir(items, recentDocs, "Office",
            "Scanner_AppCache_Expl_OfficeRecent",
            minSize: 0);
    }
}
