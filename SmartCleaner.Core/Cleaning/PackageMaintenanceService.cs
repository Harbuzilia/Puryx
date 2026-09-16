using System.Diagnostics;
using System.Text.RegularExpressions;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Cleaning;

public sealed class PackageMaintenanceService : IPackageMaintenanceService
{
    private const string ScanPathsFilename = "scan_paths.json";
    private static readonly Regex NpmPackageNamePattern = new("^(@[a-z0-9._-]+/)?[a-z0-9._-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex PipPackageNamePattern = new("^[a-z0-9._-]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IConfigService _configService;
    private readonly ICommandExecutor _commandExecutor;

    public PackageMaintenanceService(IConfigService configService)
        : this(configService, new ProcessCommandExecutor())
    {
    }

    public PackageMaintenanceService(IConfigService configService, ICommandExecutor commandExecutor)
    {
        _configService = configService;
        _commandExecutor = commandExecutor;
    }

    public Task<PackageMaintenanceResult> ExecuteAsync(IEnumerable<ScannedItem> items, CancellationToken ct = default)
        => ExecuteAsync(items, progress: null, ct);

    /// <summary>
    /// Выполнить обслуживание пакетов с отчётом о прогрессе.
    /// </summary>
    public async Task<PackageMaintenanceResult> ExecuteAsync(
        IEnumerable<ScannedItem> items,
        IProgress<CleaningProgress>? progress,
        CancellationToken ct = default)
    {
        var errors = new List<CleaningError>();
        var itemsList = items.ToList();
        var totalCount = itemsList.Count;
        var processed = 0;
        var succeeded = 0;
        var failed = 0;
        var skipped = 0;
        long freedBytes = 0;
        var allowedRoots = GetAllowedRoots();
        var started = Stopwatch.StartNew();

        foreach (var item in itemsList)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            progress?.Report(new CleaningProgress
            {
                CurrentItem = item.PackageName ?? item.Path,
                ProcessedCount = processed,
                TotalCount = totalCount
            });

            if (item.ActionTarget != CleaningActionTarget.Package)
            {
                skipped++;
                errors.Add(CreateError(item.Path, "Некорректный тип действия для package maintenance"));
                continue;
            }

            var validationError = ValidateItem(item, allowedRoots);
            if (!string.IsNullOrEmpty(validationError))
            {
                skipped++;
                errors.Add(CreateError(item.Path, validationError));
                continue;
            }

            var request = CreateRequest(item);
            if (request is null)
            {
                skipped++;
                errors.Add(CreateError(item.Path, "Недопустимая команда package manager"));
                continue;
            }

            CommandExecutionResult commandResult;
            try
            {
                commandResult = await _commandExecutor.ExecuteAsync(request, ct);
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add(CreateError(item.Path, $"Ошибка запуска команды: {ex.GetType().Name}: {ex.Message}"));
                continue;
            }

            if (commandResult.TimedOut)
            {
                failed++;
                errors.Add(CreateError(item.Path, "Превышен таймаут uninstall-команды"));
                continue;
            }

            if (commandResult.ExitCode != 0)
            {
                failed++;
                var stderr = string.IsNullOrWhiteSpace(commandResult.StandardError)
                    ? "uninstall завершился с ошибкой"
                    : commandResult.StandardError.Trim();
                errors.Add(CreateError(item.Path, stderr));
                continue;
            }

            succeeded++;
            freedBytes += item.Size;
        }

        started.Stop();

        return new PackageMaintenanceResult
        {
            ProcessedCount = processed,
            SucceededCount = succeeded,
            FailedCount = failed,
            SkippedCount = skipped,
            FreedBytes = freedBytes,
            Errors = errors,
            Duration = started.Elapsed
        };
    }

    private static CleaningError CreateError(string path, string message)
    {
        return new CleaningError
        {
            Path = path,
            Message = message,
            IsLocked = false,
            RequiresElevation = false
        };
    }

    private string? ValidateItem(ScannedItem item, IReadOnlyList<string> allowedRoots)
    {
        if (item.IsReadonlyInventory)
        {
            return "Глобальные/системные пакеты доступны только для просмотра";
        }

        if (item.PackageManager is null)
        {
            return "Не указан поддерживаемый пакетный менеджер";
        }

        if (string.IsNullOrWhiteSpace(item.PackageName))
        {
            return "Не указано имя пакета";
        }

        if (!IsValidPackageName(item.PackageManager.Value, item.PackageName))
        {
            return "Недопустимое имя пакета";
        }

        if (string.IsNullOrWhiteSpace(item.PackageWorkingDirectory))
        {
            return "Отсутствует рабочая директория проекта";
        }

        var normalizedWorkingDirectory = NormalizePath(item.PackageWorkingDirectory);
        if (!Directory.Exists(normalizedWorkingDirectory))
        {
            return "Рабочая директория не существует";
        }

        if (!IsInsideAllowedRoots(normalizedWorkingDirectory, allowedRoots))
        {
            return "Рабочая директория вне разрешенных границ";
        }

        if (IsSystemPath(normalizedWorkingDirectory))
        {
            return "Системные директории запрещены для uninstall";
        }

        if (item.PackageManager == PackageManagerType.Pip && !string.IsNullOrWhiteSpace(item.PackageExecutablePath))
        {
            var normalizedExecutable = NormalizePath(item.PackageExecutablePath);
            if (!File.Exists(normalizedExecutable))
            {
                return "Python executable для venv не найден";
            }

            if (!normalizedExecutable.StartsWith(normalizedWorkingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return "Python executable должен находиться внутри рабочей директории";
            }
        }

        return null;
    }

    private static bool IsValidPackageName(PackageManagerType manager, string packageName)
    {
        return manager switch
        {
            PackageManagerType.Npm => NpmPackageNamePattern.IsMatch(packageName),
            PackageManagerType.Pip => PipPackageNamePattern.IsMatch(packageName),
            _ => false
        };
    }

    private static CommandExecutionRequest? CreateRequest(ScannedItem item)
    {
        var packageName = item.PackageName!;
        var workingDirectory = NormalizePath(item.PackageWorkingDirectory!);

        return item.PackageManager switch
        {
            PackageManagerType.Npm => new CommandExecutionRequest
            {
                FileName = "npm",
                WorkingDirectory = workingDirectory,
                Timeout = TimeSpan.FromSeconds(30),
                Arguments = ["uninstall", packageName, "--no-audit", "--no-fund"]
            },
            PackageManagerType.Pip => new CommandExecutionRequest
            {
                FileName = ResolvePythonExecutable(item),
                WorkingDirectory = workingDirectory,
                Timeout = TimeSpan.FromSeconds(30),
                Arguments = ["-m", "pip", "uninstall", "-y", packageName]
            },
            _ => null
        };
    }

    private static string ResolvePythonExecutable(ScannedItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.PackageExecutablePath))
        {
            return NormalizePath(item.PackageExecutablePath);
        }

        return "python";
    }

    private IReadOnlyList<string> GetAllowedRoots()
    {
        var roots = _configService.Load<ScanPathsConfig>(ScanPathsFilename).Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (roots.Count == 0)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                roots.Add(NormalizePath(userProfile));
            }
            else
            {
                roots.Add(NormalizePath(Environment.CurrentDirectory));
            }
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsInsideAllowedRoots(string path, IReadOnlyList<string> allowedRoots)
    {
        return allowedRoots.Any(root =>
            path.StartsWith(EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, root, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSystemPath(string path)
    {
        var windows = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var programFiles = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var programFilesX86 = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        var commonAppData = NormalizePath(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        return IsUnder(path, windows)
               || IsUnder(path, programFiles)
               || IsUnder(path, programFilesX86)
               || IsUnder(path, commonAppData);
    }

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        return path.StartsWith(EnsureTrailingSeparator(root), StringComparison.OrdinalIgnoreCase)
               || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }
}
