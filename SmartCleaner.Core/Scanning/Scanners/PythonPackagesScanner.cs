using System.Text.Json;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Scanning.Scanners;

using PackageCommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

public sealed class PythonPackagesScanner : ScannerBase
{
    private const string ScanPathsFilename = "scan_paths.json";

    private readonly IConfigService _configService;
    private readonly PackageCommandExecutor _commandExecutor;

    public override string CategoryName => "Python Packages";
    public override string CategoryIcon => "\uE943";
    public override int DisplayOrder => 8;

    public PythonPackagesScanner(
        ISafetyService safety,
        IConfigService configService)
        : this(safety, configService, new ProcessCommandExecutor())
    {
    }

    public PythonPackagesScanner(
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
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetEffectiveScanRoots())
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Поиск python-venv в {root}...");

            foreach (var marker in EnumerateFilesSafe(root, "pyvenv.cfg", maxDepth: 5, ct))
            {
                var venvDirectory = Path.GetDirectoryName(marker);
                if (string.IsNullOrWhiteSpace(venvDirectory))
                {
                    continue;
                }

                var pythonExecutable = ResolveVenvPythonExecutable(venvDirectory);
                if (pythonExecutable is null)
                {
                    continue;
                }

                var projectDirectory = Directory.GetParent(venvDirectory)?.FullName ?? venvDirectory;
                var packageNames = await ReadPackagesAsync(pythonExecutable, projectDirectory, ct);
                foreach (var packageName in packageNames)
                {
                    var key = $"{projectDirectory}|{packageName}|venv";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    items.Add(new ScannedItem
                    {
                        Path = $"{venvDirectory}#{packageName}",
                        Size = 0,
                        LastAccess = GetLastAccess(marker),
                        Risk = RiskCategory.SafeToDelete,
                        Description = "Пакет из локального virtualenv (доступен uninstall)",
                        IsLocked = false,
                        IsDirectory = false,
                        ParentApp = $"python: {Path.GetFileName(projectDirectory)}",
                        IsSelected = false,
                        ActionTarget = CleaningActionTarget.Package,
                        PackageManager = PackageManagerType.Pip,
                        PackageName = packageName,
                        PackageWorkingDirectory = projectDirectory,
                        PackageExecutablePath = pythonExecutable,
                        IsReadonlyInventory = false
                    });
                }
            }
        }

        var globalPackages = await ReadPackagesAsync("python", Environment.CurrentDirectory, ct);
        foreach (var packageName in globalPackages)
        {
            var key = $"global|{packageName}|pip";
            if (!seen.Add(key))
            {
                continue;
            }

            items.Add(new ScannedItem
            {
                Path = $"global:pip:{packageName}",
                Size = 0,
                LastAccess = DateTime.MinValue,
                Risk = RiskCategory.UserData,
                Description = "Глобальный python-пакет (только просмотр)",
                IsLocked = false,
                IsDirectory = false,
                ParentApp = "pip global",
                IsSelected = false,
                ActionTarget = CleaningActionTarget.Package,
                PackageManager = PackageManagerType.Pip,
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

    private async Task<IReadOnlyList<string>> ReadPackagesAsync(string pythonExecutable, string workingDirectory, CancellationToken ct)
    {
        var request = new CommandExecutionRequest
        {
            FileName = pythonExecutable,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory,
            Timeout = TimeSpan.FromSeconds(8),
            Arguments = ["-m", "pip", "list", "--format=json"]
        };

        try
        {
            var result = await _commandExecutor.ExecuteAsync(request, ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return [];
            }

            using var doc = JsonDocument.Parse(result.StandardOutput);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var output = new List<string>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!entry.TryGetProperty("name", out var nameNode) || nameNode.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = nameNode.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    output.Add(name.Trim());
                }
            }

            return output
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

    private static string? ResolveVenvPythonExecutable(string venvDirectory)
    {
        var windowsPython = Path.Combine(venvDirectory, "Scripts", "python.exe");
        if (File.Exists(windowsPython))
        {
            return windowsPython;
        }

        var unixPython = Path.Combine(venvDirectory, "bin", "python");
        if (File.Exists(unixPython))
        {
            return unixPython;
        }

        return null;
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
            var name = Path.GetFileName(directory);
            var isHiddenDirectory = name.StartsWith(".", StringComparison.Ordinal)
                                    && !name.Equals(".venv", StringComparison.OrdinalIgnoreCase);
            if (isHiddenDirectory || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
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
