using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер node_modules
/// </summary>
public class NodeModulesScanner : ScannerBase
{
    private const string ScanPathsFilename = "scan_paths.json";

    private readonly IConfigService _configService;
    public override string CategoryName => "Node Modules";
    public override string CategoryIcon => "\uE8F1"; // Segoe MDL2: Package
    public override int DisplayOrder => 5;
    public override bool IsEnabledByDefault => true;

    /// <summary>
    /// Минимальный возраст папки для предложения удаления (дни)
    /// </summary>
    public int MinAgeDays { get; set; } = 14;

    /// <summary>
    /// Минимальный размер для отображения (MB)
    /// </summary>
    public int MinSizeMb { get; set; } = 50;

    /// <summary>
    /// Пути для сканирования
    /// </summary>
    public List<string> ScanPaths { get; set; } = [];

    public NodeModulesScanner(ISafetyService safety, IConfigService configService)
        : base(safety)
    {
        _configService = configService;

        ScanPaths =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ];
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            ScanPaths = GetEffectiveScanPaths();

            foreach (var scanPath in ScanPaths)
            {
                if (!Directory.Exists(scanPath))
                {
                    continue;
                }

                progress?.Report($"Поиск node_modules в {scanPath}...");
                FindNodeModules(scanPath, items, progress, ct, maxDepth: 5);
            }
        }, ct);

        // Сортируем по размеру (сначала самые большие)
        var sortedItems = items.OrderByDescending(i => i.Size).ToList();

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = sortedItems
        };
    }

    private List<string> GetEffectiveScanPaths()
    {
        var custom = _configService.Load<ScanPathsConfig>(ScanPathsFilename).Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (custom.Count == 0)
        {
            custom.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return custom;
    }

    private void FindNodeModules(string rootPath, List<ScannedItem> items,
        IProgress<string>? progress, CancellationToken ct, int maxDepth)
    {
        if (maxDepth <= 0) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(rootPath))
            {
                ct.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(dir);

                // Пропускаем скрытые и системные папки
                if (dirName.StartsWith(".") || dirName.StartsWith("$"))
                    continue;

                // Пропускаем некоторые известные папки
                if (IsSkippedFolder(dirName))
                    continue;

                if (dirName == "node_modules")
                {
                    ProcessNodeModules(dir, items, progress);
                }
                else
                {
                    // Рекурсивно ищем дальше
                    FindNodeModules(dir, items, progress, ct, maxDepth - 1);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ProcessNodeModules(string path, List<ScannedItem> items, IProgress<string>? progress)
    {
        // Пропускаем вложенные node_modules
        if (IsNestedNodeModules(path))
            return;

        try
        {
            var size = CalculateDirectorySize(path);
            var sizeMb = size / (1024 * 1024);

            // Пропускаем слишком маленькие
            if (sizeMb < MinSizeMb)
                return;

            var lastAccess = GetLastAccess(path);
            var daysSinceAccess = (DateTime.Now - lastAccess).TotalDays;

            var projectPath = Path.GetDirectoryName(path) ?? path;
            var projectName = Path.GetFileName(projectPath);

            progress?.Report($"Найден: {projectName} ({FormatSize(size)})");

            // Проверяем наличие lock-файла
            var hasLockFile = HasLockFile(projectPath);
            var restoreCommand = GetRestoreCommand(projectPath);

            // Определяем риск
            RiskCategory risk;
            string description;

            if (daysSinceAccess < MinAgeDays)
            {
                risk = RiskCategory.PerformanceCache;
                description = $"📅 Использовался {daysSinceAccess:0} дней назад — недавно активный проект";
            }
            else if (!hasLockFile)
            {
                risk = RiskCategory.PerformanceCache;
                description = "⚠️ Нет lock-файла — невозможно автоматически восстановить";
            }
            else
            {
                risk = RiskCategory.SafeToDelete;
                description = $"📅 {daysSinceAccess:0} дней без доступа";
            }

            items.Add(new ScannedItem
            {
                Path = path,
                Size = size,
                LastAccess = lastAccess,
                Risk = risk,
                Description = description,
                IsDirectory = true,
                ParentApp = $"Проект: {projectName}",
                RestoreCommand = restoreCommand,
                IsSelected = risk == RiskCategory.SafeToDelete && daysSinceAccess >= MinAgeDays
            });
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Проверить, является ли node_modules вложенным
    /// </summary>
    private static bool IsNestedNodeModules(string path)
    {
        var parent = Path.GetDirectoryName(path);
        while (!string.IsNullOrEmpty(parent))
        {
            if (Path.GetFileName(parent) == "node_modules")
                return true;
            parent = Path.GetDirectoryName(parent);
        }
        return false;
    }

    /// <summary>
    /// Проверить наличие lock-файла
    /// </summary>
    private static bool HasLockFile(string projectPath)
    {
        return File.Exists(Path.Combine(projectPath, "package-lock.json")) ||
               File.Exists(Path.Combine(projectPath, "yarn.lock")) ||
               File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")) ||
               File.Exists(Path.Combine(projectPath, "bun.lockb"));
    }

    /// <summary>
    /// Определить команду восстановления
    /// </summary>
    private static string? GetRestoreCommand(string projectPath)
    {
        if (File.Exists(Path.Combine(projectPath, "pnpm-lock.yaml")))
            return "pnpm install";
        if (File.Exists(Path.Combine(projectPath, "yarn.lock")))
            return "yarn install";
        if (File.Exists(Path.Combine(projectPath, "bun.lockb")))
            return "bun install";
        if (File.Exists(Path.Combine(projectPath, "package-lock.json")))
            return "npm install";
        if (File.Exists(Path.Combine(projectPath, "package.json")))
            return "npm install";
        
        return null;
    }

    /// <summary>
    /// Пропускаемые папки
    /// </summary>
    private static bool IsSkippedFolder(string name)
    {
        return name switch
        {
            "Windows" => true,
            "Program Files" => true,
            "Program Files (x86)" => true,
            "ProgramData" => true,
            "AppData" => true, // Не ищем в AppData — там могут быть важные node_modules
            "$Recycle.Bin" => true,
            "System Volume Information" => true,
            _ => false
        };
    }
}
