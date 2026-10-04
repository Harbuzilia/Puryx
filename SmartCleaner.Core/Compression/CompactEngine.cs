using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
// UseWindowsForms тянет System.Windows.Forms.ICommandExecutor — снимаем
// неоднозначность в пользу контракта исполнителя команд
using ICommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

namespace SmartCleaner.Core.Compression;

public class CompactEngine
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private readonly ICommandExecutor _commandExecutor;

    /// <summary>
    /// Создаёт движок поверх реального исполнителя команд (День 18, срез B).
    /// Необязательный исполнитель — шов для детерминированных тестов: стаб
    /// фиксирует команды (полное имя утилиты, аргументы, таймаут, прогресс-насос).
    /// DI-регистрация исполнителя — День 19.
    /// </summary>
    public CompactEngine(ICommandExecutor? commandExecutor = null)
    {
        _commandExecutor = commandExecutor ?? new ProcessCommandExecutor();
    }

    public async Task<List<CompactTargetItem>> DiscoverCompressibleTargetsAsync(CancellationToken ct = default)
    {
        var list = new List<CompactTargetItem>();

        await Task.Run(() =>
        {
            // 1. Scan Steam Games
            var steamRoots = BuildSteamRoots();

            foreach (var sRoot in steamRoots)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(sRoot)) continue;

                try
                {
                    foreach (var gameDir in Directory.EnumerateDirectories(sRoot))
                    {
                        var name = Path.GetFileName(gameDir);
                        var size = CalculateSize(gameDir);
                        if (size > 100 * 1024 * 1024) // > 100 MB
                        {
                            list.Add(new CompactTargetItem
                            {
                                Name = name,
                                Path = gameDir,
                                Category = "Игры (Steam)",
                                OriginalSizeBytes = size,
                                OriginalSizeFormatted = SizeFormatter.Format(size),
                                RecommendedAlgorithm = "LZX"
                            });
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CompactEngine] Steam dir enumeration error: {ex.Message}"); }
            }

            // 2. Scan Epic Games & GOG
            var epicRoots = BuildEpicRoots();
            foreach (var eRoot in epicRoots)
            {
                if (!Directory.Exists(eRoot)) continue;
                try
                {
                    foreach (var gameDir in Directory.EnumerateDirectories(eRoot))
                    {
                        var name = Path.GetFileName(gameDir);
                        var size = CalculateSize(gameDir);
                        if (size > 200 * 1024 * 1024)
                        {
                            list.Add(new CompactTargetItem
                            {
                                Name = name,
                                Path = gameDir,
                                Category = "Игры (Epic Games)",
                                OriginalSizeBytes = size,
                                OriginalSizeFormatted = SizeFormatter.Format(size),
                                RecommendedAlgorithm = "LZX"
                            });
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CompactEngine] Epic dir enumeration error: {ex.Message}"); }
            }

            // 3. Scan Developer Heavy Project Folders
            var devRoots = BuildDevRoots();

            foreach (var dRoot in devRoots)
            {
                if (!Directory.Exists(dRoot)) continue;
                try
                {
                    foreach (var projDir in Directory.EnumerateDirectories(dRoot))
                    {
                        var name = Path.GetFileName(projDir);
                        var size = CalculateSize(projDir);
                        if (size > 150 * 1024 * 1024)
                        {
                            list.Add(new CompactTargetItem
                            {
                                Name = name,
                                Path = projDir,
                                Category = "Проекты разработки",
                                OriginalSizeBytes = size,
                                OriginalSizeFormatted = SizeFormatter.Format(size),
                                RecommendedAlgorithm = "XPRESS16K"
                            });
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CompactEngine] Dev project dir enumeration error: {ex.Message}"); }
            }

        }, ct);

        return list;
    }

    public async Task<(bool Success, string Message, long SavedBytes)> CompressDirectoryAsync(
        string directoryPath, 
        string algorithm = "LZX", 
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return (false, "Директория не найдена", 0);

        // Экономия считается только по факту: размер на диске ДО запуска compact.exe
        // и ПОСЛЕ (GetCompressedFileSizeW). Никаких оценочных коэффициентов.
        var sizeOnDiskBefore = GetCompressedSizeOnDisk(directoryPath);
        progress?.Report($"Сжатие '{Path.GetFileName(directoryPath)}' алгоритмом {algorithm}...");

        var algoArg = algorithm.ToLowerInvariant() switch
        {
            "xpress4k" => "xpress4k",
            "xpress8k" => "xpress8k",
            "xpress16k" => "xpress16k",
            _ => "lzx"
        };

        try
        {
            // «/s:<путь>» — один аргумент: ArgumentList квотует его при пробелах
            // так же, как прежний Arguments с кавычками вокруг пути
            var (exitCode, timedOut, output) = await RunCompactAsync(
                ["/c", $"/s:{directoryPath}", $"/exe:{algoArg}", "/i", "/f"], progress, ct);

            if (timedOut)
                return (false, $"«compact.exe» не завершилась за {CompactToolTimeout.TotalMinutes:0} мин", 0);
            if (exitCode != 0)
                return (false, CompactFailureMessage("сжатия", exitCode, output), 0);

            // Фактическая экономия: разница размера на диске до/после сжатия.
            // Измерить не удалось — честное «н/д», а не подставное число.
            var sizeOnDiskAfter = GetCompressedSizeOnDisk(directoryPath);
            if (sizeOnDiskBefore is long before && sizeOnDiskAfter is long after)
            {
                var saved = Math.Max(0, before - after);
                return (true, $"Сжатие завершено! Сэкономлено: {SizeFormatter.Format(saved)}", saved);
            }

            return (true, "Сжатие завершено! Экономия: н/д (не удалось измерить размер на диске)", 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Отмена пользователем — не ошибка сжатия: честное сообщение
            return (false, "Операция отменена пользователем", 0);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка сжатия: {ex.Message}", 0);
        }
    }

    public async Task<(bool Success, string Message)> DecompressDirectoryAsync(
        string directoryPath, 
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        if (!Directory.Exists(directoryPath))
            return (false, "Директория не найдена");

        progress?.Report($"Распаковка '{Path.GetFileName(directoryPath)}'...");

        try
        {
            // «/s:<путь>» — один аргумент: ArgumentList квотует его при пробелах
            // так же, как прежний Arguments с кавычками вокруг пути
            var (exitCode, timedOut, output) = await RunCompactAsync(
                ["/u", $"/s:{directoryPath}", "/i"], progress, ct);

            if (timedOut)
                return (false, $"«compact.exe» не завершилась за {CompactToolTimeout.TotalMinutes:0} мин");
            if (exitCode != 0)
                return (false, CompactFailureMessage("распаковки", exitCode, output));

            return (true, "Распаковка успешно завершена!");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Отмена пользователем — не ошибка распаковки: честное сообщение
            return (false, "Операция отменена пользователем");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка распаковки: {ex.Message}");
        }
    }

    // Таймаут для compact.exe: сжатие больших каталогов легитимно долгое, поэтому запас
    // большой (час); целиком — пользователь может отменить через CancellationToken.
    private static readonly TimeSpan CompactToolTimeout = TimeSpan.FromHours(1);

    // Запускает compact.exe через исполнителя и возвращает фактический код возврата
    // и вывод (stderr приоритетнее: утилиты Microsoft пишут ошибки в разные потоки).
    // Таймаут и отмена — внутри исполнителя с kill-tree: никакого «успеха по молчанию»;
    // отмена пользователем возвращается исключением OperationCanceledException —
    // вызывающий отличает её от таймаута (TimedOut).
    private async Task<(int ExitCode, bool TimedOut, string Output)> RunCompactAsync(
        IReadOnlyList<string> arguments, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
        {
            // Абсолютный путь из системного каталога: запуск по неквалифицированному
            // имени ищет exe в каталоге приложения — binary planting (M7, День 16б)
            FileName = SystemToolLocator.GetCompactPath(),
            Arguments = arguments,
            WorkingDirectory = string.Empty,
            Timeout = CompactToolTimeout,
            // Построчный стриминг stdout: compact.exe печатает строку на каждый
            // файл — пользователь видит проценты по мере сжатия, а не после
            StandardOutputLineProgress = progress is null ? null : new CompactProgressFilter(progress)
        }, ct);

        var output = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        return (result.ExitCode, result.TimedOut, output.Trim());
    }

    /// <summary>
    /// День 18 — срез B: фильтр прогресса compact.exe. Исполнитель транслирует
    /// все непустые строки stdout, пользователю нужны только строки с процентами
    /// (compact.exe печатает строку на каждый файл). Повторяет поведение прежнего
    /// насоса PumpProgressLinesAsync. internal — для теста фильтра.
    /// </summary>
    internal sealed class CompactProgressFilter(IProgress<string> progress) : IProgress<string>
    {
        public void Report(string line)
        {
            if (!string.IsNullOrWhiteSpace(line) && line.Contains('%'))
                progress.Report(line.Trim());
        }
    }

    // Первая непустая строка вывода как компактная причина сбоя
    // (compact.exe печатает по строке на каждый файл — простыня не нужна).
    private static string CompactFailureMessage(string operation, int exitCode, string output)
    {
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim('\r', ' ');
            if (line.Length > 0)
                return $"Ошибка {operation}: compact.exe завершилась с кодом {exitCode}: {line}";
        }
        return $"Ошибка {operation}: compact.exe завершилась с кодом {exitCode}";
    }

    private static List<string> GetFixedDriveRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CompactEngine] Drive enumeration error: {ex.Message}");
            return new List<string>();
        }
    }

    private static List<string> BuildSteamRoots()
    {
        var roots = new List<string>
        {
            Path.Combine(ProgramFilesX86, "Steam", "steamapps", "common"),
            Path.Combine(ProgramFiles, "Steam", "steamapps", "common")
        };

        // Кандидатные Steam-библиотеки на всех fixed-дисках (типичные имена)
        foreach (var driveRoot in GetFixedDriveRoots())
        {
            roots.Add(Path.Combine(driveRoot, "SteamLibrary", "steamapps", "common"));
            roots.Add(Path.Combine(driveRoot, "Steam", "steamapps", "common"));
        }

        return roots;
    }

    private static List<string> BuildEpicRoots()
    {
        var roots = new List<string> { Path.Combine(ProgramFiles, "Epic Games") };

        foreach (var driveRoot in GetFixedDriveRoots())
        {
            roots.Add(Path.Combine(driveRoot, "Epic Games"));
        }

        return roots;
    }

    private static List<string> BuildDevRoots()
    {
        var roots = new List<string>
        {
            Path.Combine(UserProfile, "source", "repos"),
            Path.Combine(UserProfile, "Projects")
        };

        foreach (var driveRoot in GetFixedDriveRoots())
        {
            roots.Add(Path.Combine(driveRoot, "Projects"));
        }

        return roots;
    }

    private long CalculateSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch (Exception ex) { Debug.WriteLine($"[CompactEngine] File size error: {ex.Message}"); return 0; } });
        }
        catch (Exception ex) { Debug.WriteLine($"[CompactEngine] CalculateSize error: {ex.Message}"); return 0; }
    }

    // Фактический размер файла на диске (учитывает NTFS-сжатие), в отличие от FileInfo.Length.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private static long? GetFileCompressedSize(string path)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0)
            return null;

        return ((long)high << 32) | low;
    }

    // Суммарный фактический размер каталога на диске; null — измерить не удалось
    // (хоть один файл недоступен): тогда экономия честно «н/д», а не оценка «на глаз».
    private static long? GetCompressedSizeOnDisk(string dir)
    {
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var size = GetFileCompressedSize(file);
                if (size is null)
                {
                    Debug.WriteLine($"[CompactEngine] On-disk size unavailable: {file}");
                    return null;
                }

                total += size.Value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CompactEngine] GetCompressedSizeOnDisk error: {ex.Message}");
            return null;
        }

        return total;
    }
}
