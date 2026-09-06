using SmartCleaner.Core.Knowledge;
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
            Explanation = "Кэш скачанных npm-пакетов (.tgz архивы). Накапливается при каждом npm install. " +
                          "Удаление безопасно — пакеты скачаются заново при необходимости.",
            HowToRestore = "Автоматически: при следующем npm install пакеты скачаются с registry.npmjs.org."
        },
        new()
        {
            Name = "yarn cache",
            ParentApp = "Yarn",
            Paths = [@"%LOCALAPPDATA%\Yarn\Cache\v6", @"%LOCALAPPDATA%\Yarn\Cache"],
            CleanCommand = "yarn cache clean",
            Explanation = "Кэш скачанных пакетов Yarn. Аналог npm cache, но для Yarn.",
            HowToRestore = "Автоматически: при следующем yarn install."
        },
        new()
        {
            Name = "pnpm store",
            ParentApp = "pnpm",
            Paths = [@"%LOCALAPPDATA%\pnpm\store", @"%LOCALAPPDATA%\pnpm-store",
                     @"%USERPROFILE%\.pnpm-store"],
            CleanCommand = "pnpm store prune",
            Explanation = "Content-addressable store для pnpm. Все пакеты хранятся здесь и хардлинкуются в проекты. " +
                          "Удаление заставит pnpm скачать пакеты заново.",
            HowToRestore = "Автоматически: pnpm install скачает нужные пакеты."
        },

        // ── Python ──
        new()
        {
            Name = "pip cache",
            ParentApp = "pip (Python)",
            Paths = [@"%LOCALAPPDATA%\pip\Cache", @"%LOCALAPPDATA%\pip\cache",
                     @"%USERPROFILE%\.cache\pip"],
            CleanCommand = "pip cache purge",
            Explanation = "Кэш скачанных .whl пакетов Python. Накапливается при pip install. " +
                          "Может быть огромным если ставились тяжёлые пакеты (torch, tensorflow).",
            HowToRestore = "Автоматически: pip install скачает пакеты с PyPI."
        },
        new()
        {
            Name = "conda pkgs",
            ParentApp = "Conda/Miniconda",
            Paths = [@"%USERPROFILE%\Miniconda3\pkgs", @"%USERPROFILE%\Anaconda3\pkgs",
                     @"%USERPROFILE%\.conda\pkgs"],
            CleanCommand = "conda clean --all",
            Explanation = "Кэш пакетов Conda/Miniconda. Включает скачанные .tar.bz2 и распакованные пакеты.",
            HowToRestore = "Автоматически: conda install скачает пакеты заново."
        },

        // ── .NET ──
        new()
        {
            Name = "NuGet packages",
            ParentApp = "NuGet (.NET)",
            Paths = [@"%USERPROFILE%\.nuget\packages"],
            CleanCommand = "dotnet nuget locals global-packages --clear",
            Explanation = "Глобальный кэш NuGet-пакетов. Используется всеми .NET проектами на этой машине. " +
                          "Может занимать 2-10 ГБ если установок было много.",
            HowToRestore = "Автоматически: dotnet restore скачает нужные пакеты из nuget.org."
        },
        new()
        {
            Name = "NuGet HTTP cache",
            ParentApp = "NuGet (.NET)",
            Paths = [@"%LOCALAPPDATA%\NuGet\v3-cache", @"%LOCALAPPDATA%\NuGet\plugins-cache"],
            CleanCommand = "dotnet nuget locals http-cache --clear",
            Explanation = "HTTP-кэш NuGet — метаданные и ответы от nuget.org. Безопасно удалять.",
            HowToRestore = "Автоматически: следующий dotnet restore обновит кэш."
        },

        // ── PHP ──
        new()
        {
            Name = "Composer cache",
            ParentApp = "Composer (PHP)",
            Paths = [@"%LOCALAPPDATA%\Composer\cache", @"%APPDATA%\Composer\cache"],
            CleanCommand = "composer clear-cache",
            Explanation = "Кэш скачанных PHP-пакетов Composer.",
            HowToRestore = "Автоматически: composer install скачает зависимости."
        },

        // ── Java/Kotlin ──
        new()
        {
            Name = "Gradle cache",
            ParentApp = "Gradle (Java/Kotlin)",
            Paths = [@"%USERPROFILE%\.gradle\caches"],
            CleanCommand = "gradle --stop && del /s /q %USERPROFILE%\\.gradle\\caches",
            Explanation = "Кэш зависимостей Gradle. Включает скачанные JAR/AAR, кэш трансформаций, " +
                          "build cache. Может занимать десятки ГБ у Android-разработчиков.",
            HowToRestore = "Автоматически: gradle build скачает зависимости."
        },
        new()
        {
            Name = "Maven repository",
            ParentApp = "Maven (Java)",
            Paths = [@"%USERPROFILE%\.m2\repository"],
            CleanCommand = "mvn dependency:purge-local-repository",
            Explanation = "Локальный репозиторий Maven с JAR/POM файлами. Общий для всех Maven-проектов.",
            HowToRestore = "Автоматически: mvn install скачает зависимости из Maven Central."
        },

        // ── Go ──
        new()
        {
            Name = "Go module cache",
            ParentApp = "Go",
            Paths = [@"%LOCALAPPDATA%\go-build", @"%USERPROFILE%\go\pkg\mod\cache"],
            CleanCommand = "go clean -cache -modcache",
            Explanation = "Кэш скомпилированных Go-пакетов и скачанных модулей.",
            HowToRestore = "Автоматически: go build скачает нужные модули."
        },

        // ── Rust ──
        new()
        {
            Name = "Cargo registry",
            ParentApp = "Cargo (Rust)",
            Paths = [@"%USERPROFILE%\.cargo\registry\cache", @"%USERPROFILE%\.cargo\registry\src"],
            CleanCommand = "cargo cache --autoclean",
            Explanation = "Кэш crate-ов Rust из crates.io. Включает скачанные и распакованные исходники.",
            HowToRestore = "Автоматически: cargo build скачает нужные crate-ы."
        },

        // ── Системные кэши разработчика ──
        new()
        {
            Name = "VS Code extensions cache",
            ParentApp = "VS Code",
            Paths = [@"%USERPROFILE%\.vscode\extensions\.obsolete"],
            CleanCommand = null,
            Explanation = "Устаревшие версии расширений VS Code, помеченные для удаления.",
            HowToRestore = "Не требуется — это мусор от обновлений расширений."
        },
        new()
        {
            Name = "Electron/Chromium cache",
            ParentApp = "Electron приложения",
            Paths = [@"%APPDATA%\electron\Cache", @"%LOCALAPPDATA%\electron\Cache",
                     @"%LOCALAPPDATA%\electron-builder\Cache"],
            CleanCommand = null,
            Explanation = "Кэш Electron framework — используется Electron-приложениями (VS Code, Slack, Discord и т.д.).",
            HowToRestore = "Автоматически: скачается при сборке Electron-проектов."
        }
    ];

    public PackageCacheScanner(IKnowledgeBase knowledge, ISafetyService safety)
        : base(knowledge, safety) { }

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
                progress?.Report($"Проверка кэша: {cache.Name}...");

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

                items.Add(new ScannedItem
                {
                    Path = expandedPath,
                    Size = size,
                    LastAccess = lastAccess,
                    Risk = RiskCategory.SafeToDelete,
                    Description = $"{cache.Name} — {cache.Explanation?.Split('.')[0] ?? "кэш пакетного менеджера"}",
                    Explanation = cache.Explanation,
                    HowToRestore = cache.HowToRestore,
                    IsDirectory = true,
                    ParentApp = cache.ParentApp,
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
        public required string ParentApp { get; init; }

        /// <summary>Возможные пути (с %ENV% переменными)</summary>
        public required string[] Paths { get; init; }

        /// <summary>Команда для корректной очистки (если есть)</summary>
        public string? CleanCommand { get; init; }

        /// <summary>Объяснение: что это за кэш и зачем нужен</summary>
        public string? Explanation { get; init; }

        /// <summary>Как восстановить данные если удалил</summary>
        public string? HowToRestore { get; init; }
    }
}
