using System.Collections.Concurrent;
using System.Security.Cryptography;
using DotNet.Globbing;

namespace SmartCleaner.Core.Duplicates;

/// <summary>
/// Движок поиска дубликатов — порт Python Duplicater/scanner.py на C#.
/// Три режима: поиск дубликатов, поиск по образцу, сравнение папок.
/// Thread-safe, поддерживает CancellationToken и IProgress.
/// </summary>
public sealed class DuplicateEngine
{
    private const int BlockSize = 65536; // 64 KB
    private const int PartialSize = 65536; // 64 KB для turbo mode

    // ─────────────────────────────────────────────
    //  1. ПОИСК ДУБЛИКАТОВ
    // ─────────────────────────────────────────────

    /// <summary>
    /// Сканирует указанные директории и возвращает группы дубликатов.
    /// Алгоритм: размер → хэш → (опц.) побайтовое сравнение.
    /// </summary>
    /// <param name="directories">Список корневых папок для сканирования.</param>
    /// <param name="options">Настройки сканирования.</param>
    /// <param name="progress">Коллбэк прогресса.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Список групп дубликатов (≥2 файла в каждой).</returns>
    public async Task<List<DuplicateGroup>> ScanForDuplicatesAsync(
        IReadOnlyList<string> directories,
        DuplicateScanOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default,
        IProgress<int>? percentProgress = null)
    {
        var excludeGlobs = BuildGlobs(options.ExcludePatterns);

        // Pass 1: Собираем все файлы, группируем по размеру (0-30%)
        progress?.Report("Сканирование файлов...");
        percentProgress?.Report(0);
        var filesBySize = new Dictionary<long, List<DuplicateFile>>();
        int totalFiles = 0;

        await Task.Run(() =>
        {
            foreach (var dir in directories)
            {
                ct.ThrowIfCancellationRequested();
                CollectFiles(dir, filesBySize, excludeGlobs, options.MinFileSize, ref totalFiles, progress, ct);
            }
        }, ct);

        percentProgress?.Report(20);
        progress?.Report($"Найдено {totalFiles} файлов, фильтрация по размеру...");

        // Оставляем только группы с ≥2 файлами одного размера
        var potentialDuplicates = filesBySize
            .Where(kv => kv.Value.Count >= 2)
            .SelectMany(kv => kv.Value)
            .ToList();

        int numPotential = potentialDuplicates.Count;
        progress?.Report($"{numPotential} потенциальных дубликатов из {totalFiles} файлов");
        percentProgress?.Report(30);

        // Если нужен только размер — возвращаем сразу
        if (!options.ByHash && !options.ByName && !options.ByByte)
        {
            percentProgress?.Report(100);
            return BuildGroupsFromKey(potentialDuplicates, f => f.Size.ToString());
        }

        // Pass 2: Группируем по ключу (размер + имя + хэш) (30-80%)
        progress?.Report("Вычисление хэшей...");
        int processed = 0;

        await Task.Run(() =>
        {
            foreach (var file in potentialDuplicates)
            {
                ct.ThrowIfCancellationRequested();

                if (options.ByHash)
                {
                    file.Hash = options.TurboMode
                        ? ComputeTurboHash(file.Path)
                        : ComputeFullHash(file.Path);
                }

                processed++;
                if (processed % 50 == 0)
                {
                    progress?.Report($"Хэширование {processed}/{numPotential}...");
                    percentProgress?.Report(30 + (int)(50.0 * processed / numPotential));
                }
            }
        }, ct);

        percentProgress?.Report(80);

        var keyFunc = BuildKeyFunc(options);
        var groups = BuildGroupsFromKey(potentialDuplicates, keyFunc);

        // Pass 3: Побайтовая верификация (если включена) (80-100%)
        if (options.ByByte)
        {
            progress?.Report("Побайтовая верификация...");
            groups = await VerifyByteByByteAsync(groups, progress, ct);
        }

        percentProgress?.Report(100);
        progress?.Report($"Готово. {groups.Count} групп дубликатов.");
        return groups;
    }

    // ─────────────────────────────────────────────
    //  2. ПОИСК ПО ОБРАЗЦУ
    // ─────────────────────────────────────────────

    /// <summary>
    /// Находит копии конкретного файла в указанных директориях.
    /// </summary>
    /// <param name="samplePath">Путь к файлу-образцу.</param>
    /// <param name="searchDirectories">Где искать.</param>
    /// <param name="options">Настройки сравнения.</param>
    /// <param name="progress">Коллбэк прогресса.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Список найденных копий (без самого образца).</returns>
    public async Task<List<DuplicateFile>> ScanForSampleAsync(
        string samplePath,
        IReadOnlyList<string> searchDirectories,
        DuplicateScanOptions options,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(samplePath))
            return [];

        var sampleInfo = new FileInfo(samplePath);
        var sampleSize = sampleInfo.Length;
        var sampleName = sampleInfo.Name;
        var sampleHash = (options.ByHash || options.ByByte)
            ? (options.TurboMode ? ComputeTurboHash(samplePath) : ComputeFullHash(samplePath))
            : null;
        var sampleFullPath = Path.GetFullPath(samplePath);

        var found = new List<DuplicateFile>();
        int scanned = 0;

        await Task.Run(() =>
        {
            foreach (var dir in searchDirectories)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var filePath in SafeEnumerateFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    scanned++;

                    if (scanned % 200 == 0)
                        progress?.Report($"Проверено {scanned} файлов, найдено {found.Count} совпадений...");

                    // Пропускаем сам образец
                    if (Path.GetFullPath(filePath).Equals(sampleFullPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        var fi = new FileInfo(filePath);

                        // 1. Размер — самый быстрый фильтр
                        if (options.BySize && fi.Length != sampleSize) continue;

                        // 2. Имя
                        if (options.ByName && !fi.Name.Equals(sampleName, StringComparison.OrdinalIgnoreCase)) continue;

                        // 3. Хэш
                        if (options.ByHash)
                        {
                            var hash = options.TurboMode
                                ? ComputeTurboHash(filePath)
                                : ComputeFullHash(filePath);
                            if (hash != sampleHash) continue;
                        }

                        // 4. Побайтовое
                        if (options.ByByte && !CompareByteByByte(samplePath, filePath)) continue;

                        found.Add(new DuplicateFile
                        {
                            Path = filePath,
                            Size = fi.Length,
                            Modified = fi.LastWriteTime
                        });
                    }
                    catch (Exception) { /* пропускаем файлы без доступа */ }
                }
            }
        }, ct);

        progress?.Report($"Готово. Найдено {found.Count} копий.");
        return found;
    }

    // ─────────────────────────────────────────────
    //  3. СРАВНЕНИЕ ПАПОК
    // ─────────────────────────────────────────────

    /// <summary>
    /// Сравнивает содержимое двух папок по относительным путям.
    /// </summary>
    /// <param name="folderA">Первая папка.</param>
    /// <param name="folderB">Вторая папка.</param>
    /// <param name="progress">Коллбэк прогресса.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Результат сравнения: уникальные A, уникальные B, общие.</returns>
    public async Task<FolderCompareResult> CompareFoldersAsync(
        string folderA,
        string folderB,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report("Индексация папок...");

        var filesA = new Dictionary<string, DuplicateFile>(StringComparer.OrdinalIgnoreCase);
        var filesB = new Dictionary<string, DuplicateFile>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            IndexFolder(folderA, filesA);
            ct.ThrowIfCancellationRequested();
            IndexFolder(folderB, filesB);
        }, ct);

        var allKeys = new HashSet<string>(filesA.Keys, StringComparer.OrdinalIgnoreCase);
        allKeys.UnionWith(filesB.Keys);

        var uniqueA = new List<DuplicateFile>();
        var uniqueB = new List<DuplicateFile>();
        var common = new List<CommonFile>();

        int total = allKeys.Count;
        int processed = 0;

        foreach (var relPath in allKeys)
        {
            ct.ThrowIfCancellationRequested();
            processed++;
            if (processed % 100 == 0)
                progress?.Report($"Сравнение {processed}/{total}...");

            bool inA = filesA.TryGetValue(relPath, out var fileA);
            bool inB = filesB.TryGetValue(relPath, out var fileB);

            if (inA && !inB)
            {
                uniqueA.Add(fileA!);
            }
            else if (!inA && inB)
            {
                uniqueB.Add(fileB!);
            }
            else if (inA && inB)
            {
                double similarity = fileA!.Size == fileB!.Size ? 1.0 : 0.0;
                // Для файлов разного размера — побайтовое сравнение слишком дорогое
                // Оставляем 0.0 или 1.0 (достаточно для большинства случаев)
                if (fileA.Size == fileB.Size)
                {
                    // Быстрая проверка: одинаковый хэш = идентичны
                    var hashA = ComputeTurboHash(fileA.Path);
                    var hashB = ComputeTurboHash(fileB.Path);
                    similarity = hashA == hashB ? 1.0 : 0.5; // 0.5 = размер совпал, содержимое нет
                }

                string newer = fileA.Modified > fileB.Modified ? "A"
                    : fileB.Modified > fileA.Modified ? "B"
                    : "same";

                common.Add(new CommonFile
                {
                    RelativePath = relPath,
                    FileA = fileA,
                    FileB = fileB,
                    Similarity = similarity,
                    Newer = newer
                });
            }
        }

        progress?.Report($"Готово. A: {uniqueA.Count} уникальных, B: {uniqueB.Count} уникальных, общих: {common.Count}");

        return new FolderCompareResult
        {
            UniqueA = uniqueA,
            UniqueB = uniqueB,
            Common = common
        };
    }

    // ─────────────────────────────────────────────
    //  ХЭШИРОВАНИЕ
    // ─────────────────────────────────────────────

    /// <summary>
    /// Полный SHA256 хэш файла.
    /// </summary>
    private static string ComputeFullHash(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Turbo хэш — SHA256 первых + последних 64KB + размер файла.
    /// Аналог xxHash turbo из Python-версии, но без доп. зависимости.
    /// ~10-20× быстрее полного хэша для больших файлов (>128KB).
    /// </summary>
    private static string ComputeTurboHash(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            long fileSize = fi.Length;

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize))
            {
                // Первые 64KB
                var buffer = new byte[PartialSize];
                int read = stream.Read(buffer, 0, buffer.Length);
                sha.AppendData(buffer, 0, read);

                // Последние 64KB (если файл достаточно большой)
                if (fileSize > PartialSize * 2)
                {
                    stream.Seek(-PartialSize, SeekOrigin.End);
                    read = stream.Read(buffer, 0, buffer.Length);
                    sha.AppendData(buffer, 0, read);
                }
            }

            // Включаем размер файла для снижения коллизий
            sha.AppendData(BitConverter.GetBytes(fileSize));

            var hash = sha.GetHashAndReset();
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    // ─────────────────────────────────────────────
    //  ПОБАЙТОВОЕ СРАВНЕНИЕ
    // ─────────────────────────────────────────────

    /// <summary>
    /// Побайтовое сравнение двух файлов. True = идентичны.
    /// </summary>
    private static bool CompareByteByByte(string file1, string file2)
    {
        try
        {
            using var fs1 = new FileStream(file1, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize);
            using var fs2 = new FileStream(file2, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize);

            if (fs1.Length != fs2.Length)
                return false;

            var buf1 = new byte[BlockSize];
            var buf2 = new byte[BlockSize];

            while (true)
            {
                int read1 = fs1.Read(buf1, 0, buf1.Length);
                int read2 = fs2.Read(buf2, 0, buf2.Length);

                if (read1 != read2) return false;
                if (read1 == 0) return true;

                if (!buf1.AsSpan(0, read1).SequenceEqual(buf2.AsSpan(0, read2)))
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────────────────────────
    //  ВНУТРЕННИЕ МЕТОДЫ
    // ─────────────────────────────────────────────

    /// <summary>
    /// Рекурсивно собирает файлы из директории, группируя по размеру.
    /// </summary>
    private static void CollectFiles(
        string directory,
        Dictionary<long, List<DuplicateFile>> filesBySize,
        List<Glob>? excludeGlobs,
        long minSize,
        ref int totalFiles,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        if (!Directory.Exists(directory)) return;

        var stack = new Stack<string>();
        stack.Push(directory);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var currentDir = stack.Pop();

            // Проверяем исключения для директории
            if (ShouldExclude(currentDir, excludeGlobs))
                continue;

            // Поддиректории
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    if (!ShouldExclude(subDir, excludeGlobs))
                        stack.Push(subDir);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            // Файлы
            try
            {
                foreach (var filePath in Directory.EnumerateFiles(currentDir))
                {
                    ct.ThrowIfCancellationRequested();

                    if (ShouldExclude(filePath, excludeGlobs))
                        continue;

                    try
                    {
                        var fi = new FileInfo(filePath);
                        if (fi.Length < minSize) continue;

                        var dupFile = new DuplicateFile
                        {
                            Path = fi.FullName,
                            Size = fi.Length,
                            Modified = fi.LastWriteTime
                        };

                        if (!filesBySize.TryGetValue(fi.Length, out var list))
                        {
                            list = [];
                            filesBySize[fi.Length] = list;
                        }
                        list.Add(dupFile);

                        totalFiles++;
                        if (totalFiles % 500 == 0)
                            progress?.Report($"Найдено {totalFiles} файлов...");
                    }
                    catch (Exception) { /* skip inaccessible */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Индексирует папку по относительным путям.
    /// </summary>
    private static void IndexFolder(string folder, Dictionary<string, DuplicateFile> index)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var filePath in SafeEnumerateFiles(folder))
        {
            try
            {
                var relPath = Path.GetRelativePath(folder, filePath);
                var fi = new FileInfo(filePath);
                index[relPath] = new DuplicateFile
                {
                    Path = fi.FullName,
                    Size = fi.Length,
                    Modified = fi.LastWriteTime
                };
            }
            catch { /* skip */ }
        }
    }

    /// <summary>
    /// Безопасное рекурсивное перечисление файлов.
    /// </summary>
    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();

            // Поддиректории
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                    stack.Push(subDir);
            }
            catch { /* skip */ }

            // Файлы
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); }
            catch { continue; }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    /// <summary>
    /// Строит функцию ключа группировки по настройкам.
    /// </summary>
    private static Func<DuplicateFile, string> BuildKeyFunc(DuplicateScanOptions options)
    {
        return file =>
        {
            var parts = new List<string>(3);
            if (options.BySize) parts.Add(file.Size.ToString());
            if (options.ByName) parts.Add(file.Name.ToLowerInvariant());
            if (options.ByHash) parts.Add(file.Hash ?? "");
            return string.Join("|", parts);
        };
    }

    /// <summary>
    /// Группирует файлы по ключу, оставляя только группы ≥2.
    /// </summary>
    private static List<DuplicateGroup> BuildGroupsFromKey(
        List<DuplicateFile> files,
        Func<DuplicateFile, string> keyFunc)
    {
        return files
            .GroupBy(keyFunc)
            .Where(g => g.Count() >= 2)
            .Select(g => new DuplicateGroup
            {
                Key = g.Key,
                Files = g.ToList()
            })
            .OrderByDescending(g => g.WastedBytes)
            .ToList();
    }

    /// <summary>
    /// Побайтовая верификация: разбивает группы на подгруппы действительно идентичных файлов.
    /// </summary>
    private static async Task<List<DuplicateGroup>> VerifyByteByByteAsync(
        List<DuplicateGroup> groups,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var verified = new List<DuplicateGroup>();
        int groupIdx = 0;

        await Task.Run(() =>
        {
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                groupIdx++;
                progress?.Report($"Верификация группы {groupIdx}/{groups.Count} ({group.Files.Count} файлов)...");

                var remaining = new List<DuplicateFile>(group.Files);
                int subGroupIdx = 0;

                while (remaining.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var current = remaining[0];
                    remaining.RemoveAt(0);

                    var identical = new List<DuplicateFile> { current };
                    var notMatched = new List<DuplicateFile>();

                    foreach (var other in remaining)
                    {
                        if (CompareByteByByte(current.Path, other.Path))
                            identical.Add(other);
                        else
                            notMatched.Add(other);
                    }

                    if (identical.Count >= 2)
                    {
                        verified.Add(new DuplicateGroup
                        {
                            Key = $"{group.Key}_v{subGroupIdx}",
                            Files = identical
                        });
                        subGroupIdx++;
                    }

                    remaining = notMatched;
                }
            }
        }, ct);

        return verified;
    }

    /// <summary>
    /// Проверяет, подпадает ли путь под паттерны исключений.
    /// </summary>
    private static bool ShouldExclude(string path, List<Glob>? globs)
    {
        if (globs == null || globs.Count == 0) return false;

        var name = Path.GetFileName(path);
        foreach (var glob in globs)
        {
            if (glob.IsMatch(name) || glob.IsMatch(path))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Компилирует glob-паттерны для быстрой проверки.
    /// </summary>
    private static List<Glob>? BuildGlobs(List<string>? patterns)
    {
        if (patterns == null || patterns.Count == 0) return null;
        return patterns.Select(p => Glob.Parse(p)).ToList();
    }

    [System.Runtime.InteropServices.DllImport("Kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    /// <summary>
    /// SSS-Tier: Заменяет дубликаты на жесткие ссылки NTFS (Hardlink Zero-Byte Deduplication).
    /// Освобождает физическое пространство на диске, сохраняя файл по обоим путям.
    /// </summary>
    public async Task<(int ReplacedCount, long SpaceSavedBytes, List<string> Errors)> ReplaceDuplicatesWithHardlinksAsync(
        IEnumerable<DuplicateGroup> groups,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        int replaced = 0;
        long saved = 0;
        var errors = new List<string>();

        await Task.Run(() =>
        {
            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                if (group.Files.Count < 2) continue;

                // Первый файл выбираем как мастер-источник
                var primary = group.Files[0];
                if (!File.Exists(primary.Path)) continue;

                var primaryRoot = Path.GetPathRoot(primary.Path)?.ToUpperInvariant();

                for (int i = 1; i < group.Files.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var dup = group.Files[i];
                    if (!File.Exists(dup.Path)) continue;

                    var dupRoot = Path.GetPathRoot(dup.Path)?.ToUpperInvariant();
                    if (!string.Equals(primaryRoot, dupRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"Хардлинк невозможен между разными дисками: '{primary.Path}' и '{dup.Path}'");
                        continue;
                    }

                    try
                    {
                        progress?.Report($"Создание хардлинка для {Path.GetFileName(dup.Path)}...");
                        var tempBackup = dup.Path + $".{Guid.NewGuid():N}.bak";
                        File.Move(dup.Path, tempBackup);

                        if (CreateHardLink(dup.Path, primary.Path, IntPtr.Zero))
                        {
                            File.Delete(tempBackup);
                            replaced++;
                            saved += primary.Size;
                        }
                        else
                        {
                            File.Move(tempBackup, dup.Path);
                            errors.Add($"Ошибка CreateHardLink для '{dup.Path}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Исключение при создании хардлинка для '{dup.Path}': {ex.Message}");
                    }
                }
            }
        }, ct);

        return (replaced, saved, errors);
    }
}
