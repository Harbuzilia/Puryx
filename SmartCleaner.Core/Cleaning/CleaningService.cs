using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Cleaning;

/// <summary>
/// Реализация сервиса очистки
/// </summary>
public class CleaningService : ICleaningService
{
    private static readonly TimeSpan ElevatedRequestTtl = TimeSpan.FromMinutes(10);

    private readonly ISafetyService _safety;

    public CleaningMode Mode { get; set; } = CleaningMode.ToRecycleBin;

    public event EventHandler<CleaningResult>? CleaningCompleted;

    public CleaningService(ISafetyService safety)
    {
        _safety = safety;
    }

    public async Task<CleaningResult> CleanAsync(
        IEnumerable<ScannedItem> items,
        IProgress<CleaningProgress>? progress = null,
        CancellationToken ct = default)
    {
        var itemList = items.ToList();
        var totalBytes = itemList.Sum(x => x.Size);
        var errors = new List<CleaningError>();
        var deletedCount = 0;
        long freedBytes = 0;
        var skippedCount = 0;
        var startTime = Stopwatch.StartNew();

        for (int i = 0; i < itemList.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var item = itemList[i];

            // Баг 2: Virtual paths (docker:, global:pip:) → пропускаем, они для PackageMaintenanceService
            if (item.ActionTarget != CleaningActionTarget.FileSystem)
            {
                skippedCount++;
                continue;
            }

            // Баг 3: ReadOnly inventory (глобальные npm/pip пакеты) — только просмотр
            if (item.IsReadonlyInventory)
            {
                skippedCount++;
                continue;
            }

            var validation = _safety.ValidateForDeletion(item);

            if (!validation.CanDelete)
            {
                skippedCount++;
                errors.Add(new CleaningError
                {
                    Path = item.Path,
                    Message = validation.BlockReason ?? "Невозможно удалить",
                    IsLocked = item.IsLocked,
                    RequiresElevation = validation.RequiresElevation
                });
            }
            else if (validation.RequiresElevation)
            {
                skippedCount++;
            }
            else
            {
                var (success, error) = await DeleteItemAsync(item);

                if (success)
                {
                    deletedCount++;
                    freedBytes += item.Size;
                }
                else
                {
                    errors.Add(new CleaningError
                    {
                        Path = item.Path,
                        Message = error ?? "Неизвестная ошибка"
                    });
                }
            }

            progress?.Report(new CleaningProgress
            {
                ProcessedCount = i + 1,
                TotalCount = itemList.Count,
                ProcessedBytes = freedBytes,
                TotalBytes = totalBytes,
                CurrentItem = item.Path
            });
        }

        progress?.Report(new CleaningProgress
        {
            ProcessedCount = itemList.Count,
            TotalCount = itemList.Count,
            ProcessedBytes = freedBytes,
            TotalBytes = totalBytes,
            CurrentItem = string.Empty
        });

        startTime.Stop();

        var result = new CleaningResult
        {
            DeletedCount = deletedCount,
            FreedBytes = freedBytes,
            SkippedCount = skippedCount,
            FailedCount = errors.Count,
            Errors = errors,
            Duration = startTime.Elapsed
        };

        CleaningCompleted?.Invoke(this, result);

        return result;
    }

    public async Task<CleaningResult> CleanElevatedAsync(
        IEnumerable<ScannedItem> items,
        CancellationToken ct = default)
    {
        var errors = new List<CleaningError>();

        var eligibleItems = new List<ScannedItem>();
        foreach (var item in items)
        {
            var validation = _safety.ValidateForDeletion(item);
            if (!validation.CanDelete || !validation.RequiresElevation)
            {
                continue;
            }

            if (!ElevatedCleanTargetPolicy.IsAllowedTarget(item.Path))
            {
                errors.Add(new CleaningError
                {
                    Path = item.Path,
                    Message = "Путь не соответствует политике elevated-clean"
                });
                continue;
            }

            eligibleItems.Add(item);
        }

        if (eligibleItems.Count == 0)
        {
            return new CleaningResult
            {
                DeletedCount = 0,
                FreedBytes = 0,
                SkippedCount = errors.Count,
                FailedCount = 0,
                Errors = errors,
                Duration = TimeSpan.Zero
            };
        }

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            eligibleItems,
            ElevatedCleanTargetPolicy.CreateContract(),
            ElevatedRequestTtl,
            ct);

        try
        {
            // Обоснованное исключение (День 24, по образцу WinSxS/DriverStore/
            // Uninstaller): elevated-clean перезапускает само приложение с
            // повышением — UseShellExecute=true + Verb="runas" показывает
            // UAC-диалог; контракт передаётся файлом запроса с auth-токеном.
            // Контракт ICommandExecutor исполняет команды без элевации
            // (UseShellExecute=false, редирект потоков) — перевод этой ветки
            // на исполнителя требует расширения контракта ролью «запуск с
            // повышением»
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? "dotnet",
                Arguments = $"--elevated-clean \"{launchData.RequestFile}\" --auth-token \"{launchData.AuthToken}\"",
                UseShellExecute = true,
                Verb = "runas"
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return BuildFailedStartResult(eligibleItems, errors, "Не удалось запустить elevated-clean процесс");
            }

            await process.WaitForExitAsync(ct);

            var elevatedResult = await ElevatedCleanRequestFile.ReadResultAsync(launchData.RequestFile, ct);
            if (elevatedResult is null)
            {
                if (process.ExitCode != 0)
                {
                    errors.Add(new CleaningError
                    {
                        Path = launchData.RequestFile,
                        Message = $"Elevated-clean завершился с кодом {process.ExitCode}, результат не получен"
                    });
                }

                return BuildFallbackResult(eligibleItems, errors);
            }

            errors.AddRange(elevatedResult.Errors);

            var deletedCount = elevatedResult.DeletedCount;
            var skippedCount = elevatedResult.SkippedCount;
            var failedCount = elevatedResult.FailedCount;
            var freedBytes = eligibleItems
                .Where(item => !PathExists(item.Path))
                .Sum(item => item.Size);

            return new CleaningResult
            {
                DeletedCount = deletedCount,
                FreedBytes = freedBytes,
                SkippedCount = skippedCount + (errors.Count - elevatedResult.Errors.Count),
                FailedCount = failedCount,
                Errors = errors,
                Duration = TimeSpan.Zero
            };
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }

    public bool RequiresElevation(IEnumerable<ScannedItem> items)
    {
        return items.Any(i =>
        {
            var validation = _safety.ValidateForDeletion(i);
            return validation.RequiresElevation;
        });
    }

    /// <summary>
    /// Удалить элемент (файл или папку)
    /// </summary>
    private async Task<(bool Success, string? Error)> DeleteItemAsync(ScannedItem item)
    {
        if (item.Path.StartsWith("docker:", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteDockerDeleteAsync(item.Path);
        }

        return await Task.Run(() =>
        {
            try
            {
                if (item.IsDirectory)
                {
                    if (Mode == CleaningMode.ToRecycleBin)
                    {
                        FileSystem.DeleteDirectory(
                            item.Path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        Directory.Delete(item.Path, recursive: true);
                    }
                }
                else
                {
                    if (Mode == CleaningMode.ToRecycleBin)
                    {
                        FileSystem.DeleteFile(
                            item.Path,
                            UIOption.OnlyErrorDialogs,
                            RecycleOption.SendToRecycleBin);
                    }
                    else
                    {
                        File.Delete(item.Path);
                    }
                }

                return (true, (string?)null);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Нет доступа к файлу");
            }
            catch (IOException ex)
            {
                return (false, $"Файл заблокирован: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        });
    }

    private static async Task<(bool Success, string? Error)> ExecuteDockerDeleteAsync(string dockerPath)
    {
        try
        {
            string arguments;
            if (dockerPath.StartsWith("docker:image:", StringComparison.OrdinalIgnoreCase))
            {
                var imageId = dockerPath["docker:image:".Length..];
                if (!IsSafeDockerIdentifier(imageId))
                {
                    return (false, "Недопустимый идентификатор Docker-образа");
                }
                arguments = $"rmi -f {imageId}";
            }
            else if (dockerPath.StartsWith("docker:container:", StringComparison.OrdinalIgnoreCase))
            {
                var containerId = dockerPath["docker:container:".Length..];
                if (!IsSafeDockerIdentifier(containerId))
                {
                    return (false, "Недопустимый идентификатор Docker-контейнера");
                }
                arguments = $"rm -f {containerId}";
            }
            else if (dockerPath.Equals("docker:build-cache", StringComparison.OrdinalIgnoreCase))
            {
                arguments = "builder prune -f";
            }
            else
            {
                return (false, "Неизвестная docker команда");
            }

            // Обоснованное исключение (День 24): docker — внешний dev-инструмент,
            // его нет в системном каталоге, PATH-поиск имени — осознанная
            // семантика (инвариант M7 docker намеренно не покрывает)
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? (true, null) : (false, await process.StandardError.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Docker ID от `docker images/ps --format` — только буквы/цифры/дефисы/подчёркивания.
    /// Отсекает любые метасимволы командной строки.
    /// </summary>
    private static bool IsSafeDockerIdentifier(string id)
    {
        return id.Length is >= 4 and <= 128 &&
               id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    private static bool PathExists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    private static CleaningResult BuildFailedStartResult(
        IReadOnlyList<ScannedItem> eligibleItems,
        List<CleaningError> errors,
        string message)
    {
        errors.AddRange(eligibleItems.Select(item => new CleaningError
        {
            Path = item.Path,
            Message = message,
            RequiresElevation = true
        }));

        return new CleaningResult
        {
            DeletedCount = 0,
            FreedBytes = 0,
            SkippedCount = 0,
            FailedCount = eligibleItems.Count,
            Errors = errors,
            Duration = TimeSpan.Zero
        };
    }

    private static CleaningResult BuildFallbackResult(
        IReadOnlyList<ScannedItem> eligibleItems,
        List<CleaningError> errors)
    {
        var deletedCount = 0;
        long freedBytes = 0;

        foreach (var item in eligibleItems)
        {
            if (!PathExists(item.Path))
            {
                deletedCount++;
                freedBytes += item.Size;
            }
        }

        var failedCount = eligibleItems.Count - deletedCount;

        return new CleaningResult
        {
            DeletedCount = deletedCount,
            FreedBytes = freedBytes,
            SkippedCount = 0,
            FailedCount = failedCount,
            Errors = errors,
            Duration = TimeSpan.Zero
        };
    }
}
