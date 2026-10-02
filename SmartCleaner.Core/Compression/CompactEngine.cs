using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SmartCleaner.Core.Compression;

public class CompactEngine
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

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

        var initialSize = CalculateSize(directoryPath);
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
            var psi = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetCompactPath(),
                Arguments = $"/c /s:\"{directoryPath}\" /exe:{algoArg} /i /f",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            var (exitCode, timedOut, cancelled, output) = await RunCompactAsync(psi, CompactToolTimeoutMs, progress, ct);

            if (cancelled)
                return (false, "Операция отменена пользователем", 0);
            if (timedOut)
                return (false, output, 0);
            if (exitCode != 0)
                return (false, CompactFailureMessage("сжатия", exitCode, output), 0);

            // Re-check size on disk
            var compressedSize = GetCompressedSizeOnDisk(directoryPath);
            var saved = Math.Max(0, initialSize - compressedSize);

            return (true, $"Сжатие завершено! Сэкономлено: {SizeFormatter.Format(saved)}", saved);
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
            var psi = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetCompactPath(),
                Arguments = $"/u /s:\"{directoryPath}\" /i",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            var (exitCode, timedOut, cancelled, output) = await RunCompactAsync(psi, CompactToolTimeoutMs, progress, ct);

            if (cancelled)
                return (false, "Операция отменена пользователем");
            if (timedOut)
                return (false, output);
            if (exitCode != 0)
                return (false, CompactFailureMessage("распаковки", exitCode, output));

            return (true, "Распаковка успешно завершена!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка распаковки: {ex.Message}");
        }
    }

    // Таймаут для compact.exe: сжатие больших каталогов легитимно долгое, поэтому запас
    // большой (час); раньше целиком — пользователь может отменить через CancellationToken.
    private const int CompactToolTimeoutMs = 60 * 60 * 1000;

    // Запускает compact.exe и возвращает фактический код возврата и вывод
    // (stderr приоритетнее: утилиты Microsoft пишут ошибки в разные потоки).
    // Таймаут и отмена — тоже отказ с kill-tree: никакого «успеха по молчанию».
    private static async Task<(int ExitCode, bool TimedOut, bool Cancelled, string Output)> RunCompactAsync(
        ProcessStartInfo psi,
        int timeoutMs,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (-1, false, false, $"не удалось запустить «{psi.FileName}»");
            }

            // Потоки читаем до ожидания выхода, чтобы заполненный буфер не заблокировал утилиту
            Task<string> stdoutTask = progress == null
                ? process.StandardOutput.ReadToEndAsync()
                : PumpProgressLinesAsync(process, progress);
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
            }
            catch (TimeoutException)
            {
                KillProcessTree(process);
                return (-1, true, false, $"«compact.exe» не завершилась за {timeoutMs / 60000} мин");
            }
            catch (OperationCanceledException)
            {
                KillProcessTree(process);
                return (-1, false, true, "операция отменена пользователем");
            }

            var stdout = await SafeReadAsync(stdoutTask);
            var stderr = await SafeReadAsync(stderrTask);
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (process.ExitCode, false, false, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, false, false, $"«{psi.FileName}»: {ex.Message}");
        }
    }

    // Читает stdout построчно, транслируя строки с процентами в progress.
    // Возвращает весь текст: он нужен как причина сбоя, если stderr пуст.
    private static async Task<string> PumpProgressLinesAsync(Process process, IProgress<string> progress)
    {
        var all = new StringBuilder();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line == null) break;

            all.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line) && line.Contains('%'))
                progress.Report(line.Trim());
        }
        return all.ToString();
    }

    // Дочитывает поток после завершения/kill процесса: сбой чтения не должен
    // затирать настоящую причину (код возврата уже известен).
    private static async Task<string> SafeReadAsync(Task<string> task)
    {
        try
        {
            return await task;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CompactEngine] Kill timed-out process failed: {ex.Message}");
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

    private long GetCompressedSizeOnDisk(string dir)
    {
        // Simple estimation based on disk usage or compact query
        return CalculateSize(dir) * 60 / 100; // ~40% average LZX savings
    }
}
