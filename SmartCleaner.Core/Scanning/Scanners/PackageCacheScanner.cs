using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер кэшей пакетных менеджеров: npm, pip, yarn, pnpm, NuGet, Composer, 
/// Gradle, Maven, Go modules, Cargo/Rust. Эти кэши — крупнейший источник «невидимого» мусора.
/// </summary>
public class PackageCacheScanner : ScannerBase
{
    public override string CategoryName => "Кэши пакетов";
    public override string CategoryIcon => "\uE74C"; // Segoe MDL2: Download
    public override int DisplayOrder => 0; // Самый первый — самый важный
    public override bool IsEnabledByDefault => true;

    /// <summary>Минимальный размер кэша для отображения (50 МБ)</summary>
    private const long MinSizeBytes = 50L * 1024 * 1024;

    /// <summary>
    /// Определения кэшей пакетных менеджеров.
    /// Каждый элемент: имя, пути, команда очистки, объяснение, как восстановить.
    /// </summary>
    private static readonly CacheDefinition[] CacheDefinitions =
    [
        // ── JavaScript ──
        new()
        {
            Name = "npm cache",
            ParentApp = "npm",
            Paths = [@"%APPDATA%\npm-cache", @"%LOCALAPPDATA%\npm-cache"],
            CleanCommand = "npm cache clean --force",
            ExplanationKey = "Scanner_PackageCache_Expl_Npm",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Npm"
        },
        new()
        {
            Name = "yarn cache",
            ParentApp = "Yarn",
            Paths = [@"%LOCALAPPDATA%\Yarn\Cache\v6", @"%LOCALAPPDATA%\Yarn\Cache"],
            CleanCommand = "yarn cache clean",
            ExplanationKey = "Scanner_PackageCache_Expl_Yarn",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Yarn"
        },
        new()
        {
            Name = "pnpm store",
            ParentApp = "pnpm",
            Paths = [@"%LOCALAPPDATA%\pnpm\store", @"%LOCALAPPDATA%\pnpm-store",
                     @"%USERPROFILE%\.pnpm-store"],
            CleanCommand = "pnpm store prune",
            ExplanationKey = "Scanner_PackageCache_Expl_Pnpm",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Pnpm"
        },

        // ── Python ──
        new()
        {
            Name = "pip cache",
            ParentApp = "pip (Python)",
            Paths = [@"%LOCALAPPDATA%\pip\Cache", @"%LOCALAPPDATA%\pip\cache",
                     @"%USERPROFILE%\.cache\pip"],
            CleanCommand = "pip cache purge",
            ExplanationKey = "Scanner_PackageCache_Expl_Pip",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Pip"
        },
        new()
        {
            Name = "conda pkgs",
            ParentApp = "Conda/Miniconda",
            Paths = [@"%USERPROFILE%\Miniconda3\pkgs", @"%USERPROFILE%\Anaconda3\pkgs",
                     @"%USERPROFILE%\.conda\pkgs"],
            CleanCommand = "conda clean --all",
            ExplanationKey = "Scanner_PackageCache_Expl_Conda",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Conda"
        },

        // ── .NET ──
        new()
        {
            Name = "NuGet packages",
            ParentApp = "NuGet (.NET)",
            Paths = [@"%USERPROFILE%\.nuget\packages"],
            CleanCommand = "dotnet nuget locals global-packages --clear",
            ExplanationKey = "Scanner_PackageCache_Expl_Nuget",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Nuget"
        },
        new()
        {
            Name = "NuGet HTTP cache",
            ParentApp = "NuGet (.NET)",
            Paths = [@"%LOCALAPPDATA%\NuGet\v3-cache", @"%LOCALAPPDATA%\NuGet\plugins-cache"],
            CleanCommand = "dotnet nuget locals http-cache --clear",
            ExplanationKey = "Scanner_PackageCache_Expl_NugetHttp",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_NugetHttp"
        },

        // ── PHP ──
        new()
        {
            Name = "Composer cache",
            ParentApp = "Composer (PHP)",
            Paths = [@"%LOCALAPPDATA%\Composer\cache", @"%APPDATA%\Composer\cache"],
            CleanCommand = "composer clear-cache",
            ExplanationKey = "Scanner_PackageCache_Expl_Composer",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Composer"
        },

        // ── Java/Kotlin ──
        new()
        {
            Name = "Gradle cache",
            ParentApp = "Gradle (Java/Kotlin)",
            Paths = [@"%USERPROFILE%\.gradle\caches"],
            CleanCommand = "gradle --stop && del /s /q %USERPROFILE%\\.gradle\\caches",
            ExplanationKey = "Scanner_PackageCache_Expl_Gradle",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Gradle"
        },
        new()
        {
            Name = "Maven repository",
            ParentApp = "Maven (Java)",
            Paths = [@"%USERPROFILE%\.m2\repository"],
            CleanCommand = "mvn dependency:purge-local-repository",
            ExplanationKey = "Scanner_PackageCache_Expl_Maven",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Maven"
        },

        // ── Go ──
        new()
        {
            Name = "Go module cache",
            ParentApp = "Go",
            Paths = [@"%LOCALAPPDATA%\go-build", @"%USERPROFILE%\go\pkg\mod\cache"],
            CleanCommand = "go clean -cache -modcache",
            ExplanationKey = "Scanner_PackageCache_Expl_Go",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Go"
        },

        // ── Rust ──
        new()
        {
            Name = "Cargo registry",
            ParentApp = "Cargo (Rust)",
            Paths = [@"%USERPROFILE%\.cargo\registry\cache", @"%USERPROFILE%\.cargo\registry\src"],
            CleanCommand = "cargo cache --autoclean",
            ExplanationKey = "Scanner_PackageCache_Expl_Cargo",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Cargo"
        },

        // ── Системные кэши разработчика ──
        new()
        {
            Name = "VS Code extensions cache",
            ParentApp = "VS Code",
            Paths = [@"%USERPROFILE%\.vscode\extensions\.obsolete"],
            CleanCommand = null,
            ExplanationKey = "Scanner_PackageCache_Expl_VscodeObsolete",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_VscodeObsolete"
        },
        new()
        {
            Name = "Electron/Chromium cache",
            ParentAppKey = "Scanner_PackageCache_App_Electron",
            Paths = [@"%APPDATA%\electron\Cache", @"%LOCALAPPDATA%\electron\Cache",
                     @"%LOCALAPPDATA%\electron-builder\Cache"],
            CleanCommand = null,
            ExplanationKey = "Scanner_PackageCache_Expl_Electron",
            HowToRestoreKey = "Scanner_PackageCache_HowTo_Electron"
        }
    ];

    public PackageCacheScanner(ISafetyService safety)
        : base(safety) { }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            foreach (var cache in CacheDefinitions)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(Localizer.GetString("Scanner_PackageCache_Progress_Checking", cache.Name));

                ScanCache(cache, items);
            }
        }, ct);

        // Сортируем по размеру — самые большие первые
        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items.OrderByDescending(i => i.Size).ToList()
        };
    }

    /// <summary>
    /// Просканировать один кэш: проверить все возможные пути, суммировать размер.
    /// </summary>
    private void ScanCache(CacheDefinition cache, List<ScannedItem> items)
    {
        foreach (var pathTemplate in cache.Paths)
        {
            var expandedPath = ExpandPath(pathTemplate);
            if (!Directory.Exists(expandedPath))
                continue;

            try
            {
                var size = CalculateDirectorySize(expandedPath);
                if (size < MinSizeBytes)
                    continue;

                var lastAccess = GetLastAccess(expandedPath);

                var explanation = cache.ExplanationKey is not null
                    ? Localizer.GetString(cache.ExplanationKey)
                    : null;
                var howToRestore = cache.HowToRestoreKey is not null
                    ? Localizer.GetString(cache.HowToRestoreKey)
                    : null;

                items.Add(new ScannedItem
                {
                    Path = expandedPath,
                    Size = size,
                    LastAccess = lastAccess,
                    Risk = RiskCategory.SafeToDelete,
                    Description = $"{cache.Name} — {explanation?.Split('.')[0] ?? Localizer.GetString("Scanner_PackageCache_Desc_Default")}",
                    Explanation = explanation,
                    HowToRestore = howToRestore,
                    IsDirectory = true,
                    ParentApp = cache.ParentAppKey is not null ? Localizer.GetString(cache.ParentAppKey) : cache.ParentApp,
                    RestoreCommand = cache.CleanCommand,
                    IsSelected = true, // Кэши безопасно удалять
                    IsLocked = false
                });
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Определение кэша пакетного менеджера.
    /// </summary>
    private record CacheDefinition
    {
        /// <summary>Отображаемое имя кэша</summary>
        public required string Name { get; init; }

        /// <summary>Имя родительского приложения/менеджера</summary>
        public string? ParentApp { get; init; }

        /// <summary>Ключ локализации имени родительского приложения</summary>
        public string? ParentAppKey { get; init; }

        /// <summary>Возможные пути (с %ENV% переменными)</summary>
        public required string[] Paths { get; init; }

        /// <summary>Команда для корректной очистки (если есть)</summary>
        public string? CleanCommand { get; init; }

        /// <summary>Ключ объяснения: что это за кэш и зачем нужен</summary>
        public string? ExplanationKey { get; init; }

        /// <summary>Ключ: как восстановить данные если удалил</summary>
        public string? HowToRestoreKey { get; init; }
    }
}
