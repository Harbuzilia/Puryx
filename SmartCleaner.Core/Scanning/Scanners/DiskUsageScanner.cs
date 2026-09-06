using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер анализа крупных файлов и папок в выбранных корнях.
/// </summary>
public class DiskUsageScanner : ScannerBase
{
    private const string ScanPathsFilename = "scan_paths.json";
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> SafeExtensions = new(PathComparer)
    {
        ".tmp", ".temp", ".log", ".old", ".bak", ".dmp"
    };

    private readonly IConfigService _configService;

    public override string CategoryName => "Анализ диска";
    public override string CategoryIcon => "\uE9D9"; // Segoe MDL2: Diagnostics
    public override int DisplayOrder => 6;
    public override bool IsEnabledByDefault => true;

    /// <summary>
    /// Минимальный размер элемента для вывода в инсайты.
    /// </summary>
    public long MinInsightBytes { get; set; } = 100L * 1024 * 1024;

    /// <summary>
    /// Ограничение глубины обхода директорий.
    /// </summary>
    public int MaxDepth { get; set; } = 6;

    /// <summary>
    /// Максимальное число выводимых элементов.
    /// </summary>
    public int MaxItems { get; set; } = 30;

    public DiskUsageScanner(IKnowledgeBase knowledge, ISafetyService safety, IConfigService configService)
        : base(knowledge, safety)
    {
        _configService = configService;
    }

    public override async Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var candidates = new List<ScannedItem>();

        await Task.Run(() =>
        {
            var roots = GetEffectiveScanPaths();

            foreach (var root in roots)
            {
                ct.ThrowIfCancellationRequested();

                if (!Directory.Exists(root))
                {
                    continue;
                }

                progress?.Report($"Анализ диска: {root}");
                AnalyzeRoot(root, candidates, ct);
            }
        }, ct);

        var items = candidates
            .OrderByDescending(i => i.Size)
            .ThenBy(i => i.Path, PathComparer)
            .Take(MaxItems)
            .ToList();

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    private List<string> GetEffectiveScanPaths()
    {
        var configured = _configService.Load<ScanPathsConfig>(ScanPathsFilename).Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                try
                {
                    return Path.GetFullPath(path);
                }
                catch
                {
                    return string.Empty;
                }
            })
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .ToList();

        if (configured.Count == 0)
        {
            configured.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        }

        return configured;
    }

    private void AnalyzeRoot(string rootPath, List<ScannedItem> candidates, CancellationToken ct)
    {
        try
        {
            TraverseDirectory(new DirectoryInfo(rootPath), depth: 0, rootPath, candidates, ct);
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private long TraverseDirectory(
        DirectoryInfo directory,
        int depth,
        string rootPath,
        List<ScannedItem> candidates,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (depth > MaxDepth || !directory.Exists || IsReparsePoint(directory))
        {
            return 0;
        }

        long totalSize = 0;

        IEnumerable<FileInfo> files = [];
        try
        {
            files = directory.EnumerateFiles();
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (IsReparsePoint(file))
            {
                continue;
            }

            long size;
            try
            {
                size = file.Length;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            totalSize += size;

            if (size >= MinInsightBytes)
            {
                candidates.Add(CreateFileInsight(file, rootPath, size));
            }
        }

        IEnumerable<DirectoryInfo> subDirectories = [];
        try
        {
            subDirectories = directory.EnumerateDirectories();
        }
        catch (UnauthorizedAccessException)
        {
            return totalSize;
        }
        catch (IOException)
        {
            return totalSize;
        }

        foreach (var subDirectory in subDirectories)
        {
            ct.ThrowIfCancellationRequested();
            totalSize += TraverseDirectory(subDirectory, depth + 1, rootPath, candidates, ct);
        }

        if (depth > 0 && totalSize >= MinInsightBytes)
        {
            candidates.Add(CreateDirectoryInsight(directory, rootPath, totalSize));
        }

        return totalSize;
    }

    private ScannedItem CreateDirectoryInsight(DirectoryInfo directory, string rootPath, long size)
    {
        var risk = ClassifyRisk(directory.FullName, isDirectory: true);
        return new ScannedItem
        {
            Path = directory.FullName,
            Size = size,
            LastAccess = GetLastAccess(directory.FullName),
            Risk = risk,
            Description = BuildDescription(directory.Name, size, risk, isDirectory: true),
            IsDirectory = true,
            ParentApp = $"Корень: {rootPath}",
            IsSelected = risk == RiskCategory.SafeToDelete
        };
    }

    private ScannedItem CreateFileInsight(FileInfo file, string rootPath, long size)
    {
        var risk = ClassifyRisk(file.FullName, isDirectory: false);
        var isLocked = IsFileLocked(file.FullName);
        return new ScannedItem
        {
            Path = file.FullName,
            Size = size,
            LastAccess = GetLastAccess(file.FullName),
            Risk = isLocked ? RiskCategory.Locked : risk,
            Description = BuildDescription(file.Name, size, risk, isDirectory: false),
            IsDirectory = false,
            IsLocked = isLocked,
            ParentApp = $"Корень: {rootPath}",
            IsSelected = risk == RiskCategory.SafeToDelete && !isLocked
        };
    }

    private static bool IsReparsePoint(FileSystemInfo info)
    {
        try
        {
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return true;
        }
    }

    private static RiskCategory ClassifyRisk(string path, bool isDirectory)
    {
        var normalized = path.Replace('/', '\\').ToLowerInvariant();

        if (!isDirectory)
        {
            var extension = Path.GetExtension(path);
            if (SafeExtensions.Contains(extension))
            {
                return RiskCategory.SafeToDelete;
            }
        }

        if (normalized.Contains("\\cache") || normalized.Contains("\\logs") || normalized.Contains("\\temp") || normalized.Contains("\\tmp"))
        {
            return RiskCategory.PerformanceCache;
        }

        return RiskCategory.UserData;
    }

    private static string BuildDescription(string name, long size, RiskCategory risk, bool isDirectory)
    {
        var prefix = isDirectory ? "Крупная папка" : "Крупный файл";
        var riskHint = risk switch
        {
            RiskCategory.SafeToDelete => "можно удалить безопасно",
            RiskCategory.PerformanceCache => "кэш/временные данные, удаление может замедлить первый запуск",
            RiskCategory.UserData => "пользовательские данные, автоматическое удаление не рекомендуется",
            _ => "требуется дополнительная проверка"
        };

        return $"{prefix}: {name} ({FormatSize(size)}) — {riskHint}";
    }
}
