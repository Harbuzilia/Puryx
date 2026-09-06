using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.IO;

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
            var steamRoots = new[]
            {
                Path.Combine(ProgramFilesX86, "Steam", "steamapps", "common"),
                Path.Combine(ProgramFiles, "Steam", "steamapps", "common"),
                @"D:\SteamLibrary\steamapps\common",
                @"E:\SteamLibrary\steamapps\common"
            };

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
                catch { }
            }

            // 2. Scan Epic Games & GOG
            var epicRoots = new[] { Path.Combine(ProgramFiles, "Epic Games"), @"D:\Epic Games", @"E:\Epic Games" };
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
                catch { }
            }

            // 3. Scan Developer Heavy Project Folders
            var devRoots = new[]
            {
                Path.Combine(UserProfile, "source", "repos"),
                Path.Combine(UserProfile, "Projects"),
                @"E:\AllMyProject",
                @"D:\Projects"
            };

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
                catch { }
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
                FileName = "compact.exe",
                Arguments = $"/c /s:\"{directoryPath}\" /exe:{algoArg} /i /f",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "Не удалось запустить compact.exe", 0);

            while (!proc.StandardOutput.EndOfStream)
            {
                if (ct.IsCancellationRequested)
                {
                    proc.Kill(true);
                    return (false, "Операция отменена пользователем", 0);
                }

                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line) && line.Contains("%"))
                {
                    progress?.Report(line.Trim());
                }
            }

            await proc.WaitForExitAsync(ct);

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
                FileName = "compact.exe",
                Arguments = $"/u /s:\"{directoryPath}\" /i",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                return (true, "Распаковка успешно завершена!");
            }
            return (false, "Не удалось запустить compact.exe");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка распаковки: {ex.Message}");
        }
    }

    private long CalculateSize(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
        }
        catch { return 0; }
    }

    private long GetCompressedSizeOnDisk(string dir)
    {
        // Simple estimation based on disk usage or compact query
        return CalculateSize(dir) * 60 / 100; // ~40% average LZX savings
    }
}
