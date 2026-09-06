using System.Diagnostics;
using SmartCleaner.Core.Helpers;

namespace SmartCleaner.Core.LargeFiles;

/// <summary>
/// Результат сканирования — один большой файл.
/// </summary>
public sealed class LargeFileInfo
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Directory { get; set; } = "";
    public long Size { get; set; }
    public string SizeFormatted => SizeFormatter.Format(Size);
    public DateTime Modified { get; set; }
    public string Extension { get; set; } = "";
}

/// <summary>
/// Движок поиска больших файлов на диске.
/// Находит Top-N файлов по размеру в указанных каталогах.
/// </summary>
public sealed class LargeFilesEngine
{
    /// <summary>
    /// Сканирует папку и возвращает Top-N самых больших файлов.
    /// </summary>
    /// <param name="rootPath">Корневая папка для сканирования.</param>
    /// <param name="topN">Количество файлов в результате.</param>
    /// <param name="minSizeBytes">Минимальный размер файла (фильтр).</param>
    /// <param name="progress">Текстовый прогресс.</param>
    /// <param name="ct">Токен отмены.</param>
    public async Task<List<LargeFileInfo>> ScanAsync(
        string rootPath,
        int topN = 50,
        long minSizeBytes = 1024 * 1024, // 1 МБ по умолчанию
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report($"Сканирование: {rootPath}...");

        // Используем SortedSet с ограничением по размеру для эффективности
        var result = new SortedSet<LargeFileInfo>(Comparer<LargeFileInfo>.Create(
            (a, b) =>
            {
                int cmp = b.Size.CompareTo(a.Size); // desc by size
                return cmp != 0 ? cmp : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            }));

        int totalScanned = 0;
        long minThreshold = minSizeBytes;

        await Task.Run(() =>
        {
            ScanDirectory(rootPath, result, topN, ref minThreshold, ref totalScanned, progress, ct);
        }, ct);

        progress?.Report($"Готово. Просканировано {totalScanned:N0} файлов.");
        return result.Take(topN).ToList();
    }

    /// <summary>
    /// Рекурсивное сканирование каталога.
    /// </summary>
    private static void ScanDirectory(
        string dirPath,
        SortedSet<LargeFileInfo> result,
        int topN,
        ref long minThreshold,
        ref int totalScanned,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        try
        {
            // Файлы
            foreach (var filePath in System.IO.Directory.EnumerateFiles(dirPath))
            {
                ct.ThrowIfCancellationRequested();
                totalScanned++;

                try
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Length < minThreshold) continue;

                    result.Add(new LargeFileInfo
                    {
                        Path = fi.FullName,
                        Name = fi.Name,
                        Directory = fi.DirectoryName ?? "",
                        Size = fi.Length,
                        Modified = fi.LastWriteTime,
                        Extension = fi.Extension.ToLowerInvariant()
                    });

                    // Если набрали больше topN — поднимаем порог
                    if (result.Count > topN * 2)
                    {
                        while (result.Count > topN)
                            result.Remove(result.Max!);

                        minThreshold = result.Max?.Size ?? minThreshold;
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[LargeFiles] Skip file: {ex.Message}"); }

                if (totalScanned % 5000 == 0)
                    progress?.Report($"Просканировано {totalScanned:N0} файлов...");
            }

            // Подкаталоги
            foreach (var subDir in System.IO.Directory.EnumerateDirectories(dirPath))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attrs = File.GetAttributes(subDir);
                    if ((attrs & FileAttributes.ReparsePoint) != 0) continue;

                    ScanDirectory(subDir, result, topN, ref minThreshold, ref totalScanned, progress, ct);
                }
                catch (Exception ex) { Debug.WriteLine($"[LargeFiles] Skip dir: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[LargeFiles] Root access error: {ex.Message}"); }
    }
}
