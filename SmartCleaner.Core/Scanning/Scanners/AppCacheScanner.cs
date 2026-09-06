using SmartCleaner.Core.Knowledge;
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

    public AppCacheScanner(IKnowledgeBase knowledge, ISafetyService safety) 
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // ═══════════════ МЕССЕНДЖЕРЫ ═══════════════

            progress?.Report("Сканирование: Discord...");
            ScanElectronApp(items, appData, "discord", "Discord",
                "Кэш чатов, аватаров, вложений Discord",
                "Перекачается автоматически при открытии чатов");

            progress?.Report("Сканирование: Telegram...");
            ScanDir(items, Path.Combine(appData, "Telegram Desktop", "tdata", "user_data", "cache"),
                "Telegram", "Кэш медиа Telegram (фото, видео, стикеры)",
                "Медиа перекачается из облака при просмотре");
            ScanDir(items, Path.Combine(appData, "Telegram Desktop", "tdata", "user_data", "media_cache"),
                "Telegram", "Кэш медиа Telegram",
                "Перекачается автоматически");

            progress?.Report("Сканирование: WhatsApp...");
            ScanElectronApp(items, localAppData, "WhatsApp", "WhatsApp",
                "Кэш WhatsApp Desktop",
                "Перекачается при синхронизации с телефоном");

            progress?.Report("Сканирование: Slack...");
            ScanElectronApp(items, appData, "Slack", "Slack",
                "Кэш рабочих пространств Slack",
                "Перекачается при открытии каналов");

            progress?.Report("Сканирование: Teams...");
            ScanElectronApp(items, appData, @"Microsoft\Teams", "Microsoft Teams",
                "Кэш чатов и файлов Teams",
                "Перекачается при открытии чатов");
            // Новый Teams
            ScanElectronApp(items, localAppData, @"Packages\MSTeams_8wekyb3d8bbwe\LocalCache", "Teams (new)",
                "Кэш нового Microsoft Teams",
                "Перекачается автоматически");

            progress?.Report("Сканирование: Zoom...");
            ScanDir(items, Path.Combine(appData, "Zoom", "data"),
                "Zoom", "Кэш данных Zoom",
                "Перекачается при следующем звонке");

            progress?.Report("Сканирование: Skype...");
            ScanElectronApp(items, appData, @"Microsoft\Skype for Desktop", "Skype",
                "Кэш Skype Desktop",
                "Перекачается при открытии чатов");

            // ═══════════════ МЕДИА ═══════════════

            progress?.Report("Сканирование: Spotify...");
            ScanDir(items, Path.Combine(localAppData, "Spotify", "Storage"),
                "Spotify", "Оффлайн-кэш музыки Spotify",
                "Музыка перекачается при прослушивании. Оффлайн-плейлисты нужно пересохранить",
                howToRestore: "Оффлайн-плейлисты: заново скачать в настройках Spotify");
            ScanDir(items, Path.Combine(localAppData, "Spotify", "Data"),
                "Spotify", "Кэш данных Spotify",
                "Перекачается автоматически");

            progress?.Report("Сканирование: Steam...");
            ScanDir(items, Path.Combine(localAppData, "Steam", "htmlcache"),
                "Steam", "Кэш HTML/веб-контента Steam",
                "Перекачается при открытии магазина");
            // Steam shader cache (в каждой игре)
            var steamPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 
                "Steam", "steamapps", "shadercache");
            ScanDir(items, steamPath,
                "Steam", "Предкомпилированные шейдеры игр Steam",
                "Перекомпилируются при первом запуске каждой игры (может быть подвисание 1-2 мин)");

            progress?.Report("Сканирование: Epic Games...");
            ScanDir(items, Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "webcache"),
                "Epic Games", "Кэш веб-контента Epic Games Launcher",
                "Перекачается при открытии лаунчера");
            ScanDir(items, Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Logs"),
                "Epic Games", "Логи Epic Games Launcher",
                "Создадутся заново при следующем запуске");

            progress?.Report("Сканирование: Adobe...");
            ScanAdobeCaches(items, appData, localAppData);

            progress?.Report("Сканирование: VLC...");
            ScanDir(items, Path.Combine(appData, "vlc", "art", "artistalbum"),
                "VLC", "Кэш обложек VLC",
                "Перекачается при воспроизведении");

            // ═══════════════ IDE / РЕДАКТОРЫ ═══════════════

            progress?.Report("Сканирование: JetBrains IDE...");
            ScanJetBrainsCaches(items, localAppData);

            progress?.Report("Сканирование: Visual Studio...");
            ScanVisualStudioCaches(items, localAppData);

            progress?.Report("Сканирование: Android Studio...");
            var androidCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "cache");
            ScanDir(items, androidCache,
                "Android Studio", "Кэш Android SDK",
                "Перекачается при следующей сборке");

            // ═══════════════ ОФИС ═══════════════

            progress?.Report("Сканирование: Microsoft Office...");
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
    /// <param name="description">Описание кэша</param>
    /// <param name="howToRestore">Инструкция восстановления</param>
    private void ScanElectronApp(List<ScannedItem> items, string basePath, string appFolder,
        string appName, string description, string howToRestore)
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
                Explanation = description,
                HowToRestore = howToRestore,
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
        string description, string explanation, string? howToRestore = null, 
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
            Explanation = explanation,
            HowToRestore = howToRestore ?? "Пересоздаётся автоматически при запуске приложения",
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
                        $"Кэш Adobe {productName}",
                        "Медиа-кэш Adobe. Перестроится при открытии проектов",
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
                    $"Кэш/логи {ideName}",
                    "Кэш индексов и логи IDE. Переиндексация при первом запуске (2-5 мин)",
                    howToRestore: "Пересоздастся при запуске IDE. Первая индексация займёт несколько минут",
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
                    $"Кэш {folder} Visual Studio",
                    "Кэш компонентов VS. Пересоздаётся при первом запуске после удаления",
                    minSize: 2 * 1024 * 1024);
            }
        }

        // MEF ComponentModelCache в Exp
        var mefCache = Path.Combine(vsRoot, "MeowComponent");
        ScanDir(items, mefCache, "Visual Studio", "MEF кэш", 
            "Кэш MEF-компонентов Visual Studio");
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
                        $"Кэш Office ({folder})",
                        "Кэш файлов Office. Документы не пострадают",
                        minSize: 512 * 1024);
                }
            }
        }

        // Outlook кэш
        var outlookCache = Path.Combine(localAppData, "Microsoft", "Outlook", "RoamCache");
        ScanDir(items, outlookCache, "Outlook",
            "Кэш Outlook (RoamCache)",
            "Кэш вложений и превью. Перекачается при открытии писем");

        // OneNote кэш
        var onenoteCache = Path.Combine(localAppData, "Microsoft", "OneNote");
        if (Directory.Exists(onenoteCache))
        {
            foreach (var dir in SafeEnumerateDirectories(onenoteCache))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("cache", StringComparison.OrdinalIgnoreCase))
                {
                    ScanDir(items, dir, "OneNote", "Кэш OneNote",
                        "Кэш синхронизации. Записи синхронизируются из облака");
                }
            }
        }

        // Recent файлы Office
        var recentDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Office", "Recent");
        ScanDir(items, recentDocs, "Office",
            "Список последних файлов Office",
            "Ярлыки к недавно открытым документам. Документы НЕ удаляются",
            minSize: 0);
    }
}
