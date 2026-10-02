using System.Text.Json;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Scanning.Scanners;

using PackageCommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

public sealed class NpmPackagesScanner : ScannerBase
{
    private const string ScanPathsFilename = "scan_paths.json";

    private readonly IConfigService _configService;
    private readonly PackageCommandExecutor _commandExecutor;

    public override string CategoryName => "NPM Packages";
    public override string CategoryIcon => "\uE8F1";
    public override int DisplayOrder => 7;

    public NpmPackagesScanner(
        ISafetyService safety,
        IConfigService configService)
        : this(safety, configService, new ProcessCommandExecutor())
    {
    }

    public NpmPackagesScanner(
        ISafetyService safety,
        IConfigService configService,
        PackageCommandExecutor commandExecutor)
        : base(safety)
    {
        _configService = configService;
        _commandExecutor = commandExecutor;
    }

    public override async Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();
        var seenLocal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetEffectiveScanRoots())
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Поиск npm-пакетов в {root}...");

            foreach (var packageJson in EnumerateFilesSafe(root, "package.json", maxDepth: 4, ct))
            {
                ExtractLocalPackages(packageJson, seenLocal, items);
            }
        }

        var globalPackages = await ReadGlobalPackagesAsync(ct);
        foreach (var packageName in globalPackages)
        {
            items.Add(new ScannedItem
            {
                Path = $"global:npm:{packageName}",
                Size = 0,
                LastAccess = DateTime.MinValue,
                Risk = RiskCategory.UserData,
                Description = "Глобальный npm-пакет (только просмотр)",
                IsLocked = false,
                IsDirectory = false,
                ParentApp = "npm global",
                IsSelected = false,
                ActionTarget = CleaningActionTarget.Package,
                PackageManager = PackageManagerType.Npm,
                PackageName = packageName,
                PackageWorkingDirectory = null,
                IsReadonlyInventory = true
            });
        }

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
                .OrderBy(i => i.IsReadonlyInventory)
                .ThenBy(i => i.PackageWorkingDirectory)
                .ThenBy(i => i.PackageName)
                .ToList()
        };
    }

    private void ExtractLocalPackages(string packageJsonPath, HashSet<string> seenLocal, List<ScannedItem> items)
    {
        try
        {
            using var stream = File.OpenRead(packageJsonPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var projectDirectory = Path.GetDirectoryName(packageJsonPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return;
            }

            foreach (var dependencyGroup in new[] { "dependencies", "devDependencies", "optionalDependencies", "peerDependencies" })
            {
                if (!root.TryGetProperty(dependencyGroup, out var dependencies) || dependencies.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    var packageName = dependency.Name.Trim();
                    if (string.IsNullOrWhiteSpace(packageName))
                    {
                        continue;
                    }

                    var key = $"{projectDirectory}|{packageName}";
                    if (!seenLocal.Add(key))
                    {
                        continue;
                    }

                    items.Add(new ScannedItem
                    {
                        Path = $"{packageJsonPath}#{packageName}",
                        Size = 0,
                        LastAccess = GetLastAccess(packageJsonPath),
                        Risk = RiskCategory.SafeToDelete,
                        Description = "Локальный npm-пакет проекта (доступен uninstall)",
                        IsLocked = false,
                        IsDirectory = false,
                        ParentApp = $"npm: {Path.GetFileName(projectDirectory)}",
                        IsSelected = false,
                        ActionTarget = CleaningActionTarget.Package,
                        PackageManager = PackageManagerType.Npm,
                        PackageName = packageName,
                        PackageWorkingDirectory = projectDirectory,
                        IsReadonlyInventory = false
                    });
                }
            }
        }
        catch
        {
        }
    }

    private async Task<IReadOnlyList<string>> ReadGlobalPackagesAsync(CancellationToken ct)
    {
        var request = new CommandExecutionRequest
        {
            FileName = "npm",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Timeout = TimeSpan.FromSeconds(8),
            Arguments = ["ls", "-g", "--depth=0", "--json"]
        };

        try
        {
            var result = await _commandExecutor.ExecuteAsync(request, ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return [];
            }

            using var doc = JsonDocument.Parse(result.StandardOutput);
            if (!doc.RootElement.TryGetProperty("dependencies", out var deps)
                || deps.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            return deps.EnumerateObject()
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private IReadOnlyList<string> GetEffectiveScanRoots()
    {
        var roots = _configService.Load<ScanPathsConfig>(ScanPathsFilename).Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roots.Count == 0)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                roots.Add(userProfile);
            }
        }

        return roots;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string fileName, int maxDepth, CancellationToken ct)
    {
        if (maxDepth < 0 || !Directory.Exists(root))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, fileName, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            yield return file;
        }

        if (maxDepth == 0)
        {
            yield break;
        }

        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            ct.ThrowIfCancellationRequested();
            var dirName = Path.GetFileName(directory);
            if (dirName.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
                || dirName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var nested in EnumerateFilesSafe(directory, fileName, maxDepth - 1, ct))
            {
                yield return nested;
            }
        }
    }
}
