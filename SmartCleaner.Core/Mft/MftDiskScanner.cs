using System.Diagnostics;
using SmartCleaner.Core.DiskMap;
using SmartCleaner.Core.LargeFiles;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Mft;

/// <summary>Вид локализованной причины отказа MFT-режима (День 9, ROADMAP v3.1).</summary>
public enum MftScanFailureKind
{
    /// <summary>Отказа не было — скан успешен.</summary>
    None = 0,

    /// <summary>MFT отключён настройкой (MftScanMode = Off).</summary>
    Disabled,

    /// <summary>Том не NTFS (или не открылся): MFT неприменим.</summary>
    NotNtfs,

    /// <summary>Нет прав на чтение $MFT: нужна элевация, честный фолбэк на обход.</summary>
    AccessDenied,

    /// <summary>Ошибка чтения/структуры тома при перечислении $MFT.</summary>
    VolumeError
}

/// <summary>
/// Результат попытки MFT-скана тома (День 9): успех — дерево + статистика;
/// отказ — классифицированная локализованная причина для статуса скана.
/// </summary>
public sealed class MftScanResult
{
    /// <summary>true — Tree построено; иначе см. <see cref="FailureKind"/>/<see cref="FailureText"/>.</summary>
    public bool Success { get; init; }

    /// <summary>Дерево файлов тома (null при отказе).</summary>
    public MftTree? Tree { get; init; }

    /// <summary>Число перечисленных MFT-записей (включая пропущенные/битые).</summary>
    public long RecordCount { get; init; }

    /// <summary>Длительность перечисления + сборки дерева.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Фактический режим скана: "MFT" (маркер, не локализуется).</summary>
    public string Mode { get; init; } = "";

    /// <summary>Вид отказа; None при успехе.</summary>
    public MftScanFailureKind FailureKind { get; init; }

    /// <summary>Локализованный текст причины отказа (null при успехе).</summary>
    public string? FailureText { get; init; }
}

/// <summary>
/// Фасад MFT-сканирования (День 9, ROADMAP v3.1, фаза M): одна точка входа
/// для DiskUsageScanner/LargeFilesEngine/DiskMapEngine. Читает $MFT тома,
/// строит дерево <see cref="MftTree"/> и конвертирует его в модели сканеров
/// (pure-функции, тестируются синтетикой). Выбор режима — единая настройка
/// <see cref="ScanSettingsConfig"/> (файл <see cref="SettingsFilename"/>):
/// Auto (NTFS+права → MFT) | On | Off; неудача — честный фолбэк на обход
/// каталогов с причиной в статусе. Элевацию НЕ создаём: без прав — фолбэк
/// и подсказка; полноценный elevated-анализатор — вне v3.1.
/// </summary>
public class MftDiskScanner
{
    /// <summary>Имя файла единой настройки сканирования (единая точка для Core и App).</summary>
    public const string SettingsFilename = ScanSettingsConfig.Filename;

    private readonly IConfigService? _configService;

    /// <summary>DI-конструктор; null конфиг-сервиса → предпочтение Auto (только чтение).</summary>
    public MftDiskScanner(IConfigService? configService = null)
    {
        _configService = configService;
    }

    /// <summary>
    /// Пробует отсканировать том по корню пути ("C:\" или любой путь на томе):
    /// читает $MFT и строит дерево. Отказ — не исключение, а результат с
    /// классифицированной локализованной причиной.
    /// </summary>
    /// <param name="driveRoot">Корень тома или любой путь на нём (буква из корня).</param>
    /// <param name="ct">Токен отмены (проверяется между записями).</param>
    public MftScanResult TryScanDrive(string driveRoot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(driveRoot);

        string preference = NormalizePreference(LoadPreference());

        if (preference == "Off")
        {
            return Failed(MftScanFailureKind.Disabled, Localizer.GetString("Mft_Fallback_Disabled"));
        }

        char letter = ExtractDriveLetter(driveRoot);
        if (letter == '\0')
        {
            return Failed(
                MftScanFailureKind.VolumeError,
                Localizer.GetString("Mft_Fallback_Error", $"не распознан путь тома: {driveRoot}"));
        }

        // Auto: дешёвая проверка NTFS (без прав) до попытки открытия тома
        if (preference == "Auto" && !IsNtfsVolume(letter))
        {
            return Failed(MftScanFailureKind.NotNtfs, Localizer.GetString("Mft_Fallback_NotNtfs"));
        }

        IMftVolumeReader? reader = OpenVolumeReader(letter);
        if (reader == null)
        {
            // том принципиально не открылся: не NTFS / диск отсутствует
            return Failed(MftScanFailureKind.NotNtfs, Localizer.GetString("Mft_Fallback_NotNtfs"));
        }

        using (reader)
        {
            if (!reader.CanRead)
            {
                return reader.FailureKind switch
                {
                    MftReaderFailure.AccessDenied => Failed(
                        MftScanFailureKind.AccessDenied, Localizer.GetString("Mft_Fallback_Rights")),
                    _ => Failed(
                        MftScanFailureKind.VolumeError,
                        Localizer.GetString("Mft_Fallback_Error", reader.FailureReason ?? ""))
                };
            }

            var recordCounter = new RecordCounter();
            var stopwatch = Stopwatch.StartNew();
            MftTree tree;
            try
            {
                // стриминг записей без буферизации списка: Build съедает
                // перечисление ридера, счётчик инкрементируется в конвейере
                tree = MftTreeBuilder.Build(CountRecords(reader.EnumerateRecords(ct), recordCounter));
            }
            catch (OperationCanceledException)
            {
                throw; // отмена пробрасывается вызывающему сканеру
            }
            catch (Exception ex)
            {
                return Failed(
                    MftScanFailureKind.VolumeError,
                    Localizer.GetString("Mft_Fallback_Error", ex.Message));
            }
            stopwatch.Stop();

            return new MftScanResult
            {
                Success = true,
                Tree = tree,
                RecordCount = recordCounter.Count,
                Elapsed = stopwatch.Elapsed,
                Mode = "MFT"
            };
        }
    }

    /// <summary>
    /// Шов открытия ридера тома (тесты подменяют): null — том принципиально
    /// не открылся (не NTFS/нет диска); ридер с CanRead = false — отказ прав
    /// или структурная проблема (классификация по <see cref="IMftVolumeReader.FailureKind"/>).
    /// </summary>
    /// <param name="letter">Буква тома (A–Z, верхний регистр).</param>
    protected virtual IMftVolumeReader? OpenVolumeReader(char letter)
    {
        return MftVolume.TryOpenVolume(letter, out IMftVolumeReader? reader, out _) ? reader : null;
    }

    /// <summary>
    /// Шов дешёвой проверки NTFS без открытия тома (DriveInfo.DriveFormat,
    /// прав не требует) — префектор режима Auto до попытки чтения $MFT.
    /// </summary>
    /// <param name="letter">Буква тома (A–Z, верхний регистр).</param>
    protected virtual bool IsNtfsVolume(char letter)
    {
        try
        {
            var drive = new DriveInfo(letter.ToString());
            return drive.IsReady && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ---------- конверсии дерева в модели сканеров (pure) ----------

    /// <summary>
    /// Конверсия дерева в топ-N больших файлов (как LargeFilesEngine):
    /// путь = префикс поддерева + имена; размер = итоговый размер узла
    /// (Allocated &gt; 0 ? Allocated : Real); Modified — default (движок
    /// обогащает best-effort только для финальных top-N). Порядок: размер
    /// desc, затем путь asc (OrdinalIgnoreCase) — детерминированно.
    /// </summary>
    /// <param name="subtree">Поддерево (обычно корень тома или найданный по пути узел).</param>
    /// <param name="subtreeRootPath">Путь поддерева (префикс путей файлов).</param>
    /// <param name="minSize">Минимальный размер файла в байтах.</param>
    /// <param name="maxCount">Максимум возвращаемых файлов (Top-N).</param>
    public static List<LargeFileInfo> ToLargeFiles(
        MftTreeNode subtree,
        string subtreeRootPath,
        long minSize = 1024 * 1024,
        int maxCount = 50)
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(subtreeRootPath);
        ArgumentOutOfRangeException.ThrowIfNegative(minSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maxCount);

        var files = new List<LargeFileInfo>();
        CollectFiles(subtree, subtreeRootPath, minSize, files);
        if (files.Count == 0)
            return files;

        files.Sort(static (a, b) =>
        {
            int cmp = b.Size.CompareTo(a.Size); // desc по размеру
            return cmp != 0 ? cmp : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        });

        if (files.Count > maxCount)
            files.RemoveRange(maxCount, files.Count - maxCount);
        return files;
    }

    /// <summary>
    /// Конверсия дерева в папки ≥ порога — «как DiskUsageScanner выдаёт»:
    /// ScannedItem с Risk/Description по той же логике; корень поддерева
    /// сам не выдаётся (зеркало walk-режима: только depth &gt; 0).
    /// LastAccess — default (сканер обогащает для финальных top-N).
    /// </summary>
    /// <param name="subtree">Поддерево (обычно узел, найденный по пути сканирования).</param>
    /// <param name="subtreeRootPath">Путь поддерева (префикс путей + ParentApp-корень).</param>
    /// <param name="minBytes">Минимальный итоговый размер папки в байтах.</param>
    public static List<ScannedItem> ToFolderItems(
        MftTreeNode subtree,
        string subtreeRootPath,
        long minBytes)
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(subtreeRootPath);
        ArgumentOutOfRangeException.ThrowIfNegative(minBytes);

        var items = new List<ScannedItem>();
        CollectFolders(subtree, subtreeRootPath, subtreeRootPath, minBytes, items);
        return items;
    }

    /// <summary>
    /// Конверсия дерева в узлы DiskNode для DiskMapEngine (зеркало
    /// BuildNode/CalculatePercentages: файлы на каждом уровне, спуск в
    /// подпапки до maxDepth, сортировка детей по размеру desc, проценты
    /// от родителя; на пределе глубины подпапки узла не добавляются).
    /// </summary>
    /// <param name="subtree">Поддерево (корень тома или узел по пути).</param>
    /// <param name="rootPath">Путь поддерева (FullPath корневого узла).</param>
    /// <param name="maxDepth">Максимум глубины (0 = без ограничений).</param>
    public static DiskNode ToTreemapInput(
        MftTreeNode subtree,
        string rootPath,
        int maxDepth = 0)
    {
        ArgumentNullException.ThrowIfNull(subtree);
        ArgumentNullException.ThrowIfNull(rootPath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);

        var root = new DiskNode
        {
            Name = ResolveRootName(subtree, rootPath),
            FullPath = rootPath,
            IsFile = false,
            Depth = 0
        };
        root.Size = BuildTreemapChildren(subtree, rootPath, depth: 0, maxDepth, root.Children);
        CalculateTreemapPercentages(root);
        return root;
    }

    /// <summary>
    /// Ищет узел поддерева по абсолютному пути (навигация по детям
    /// OrdinalIgnoreCase, сегменты — только каталоги). null — пути нет
    /// в дереве (в т.ч. вне тома).
    /// </summary>
    /// <param name="tree">Дерево тома.</param>
    /// <param name="volumeRoot">Корень тома ("C:\").</param>
    /// <param name="absolutePath">Абсолютный путь на томе.</param>
    public static MftTreeNode? FindSubtree(MftTree tree, string volumeRoot, string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(volumeRoot);
        ArgumentNullException.ThrowIfNull(absolutePath);
        if (tree.Root == null)
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(absolutePath);
        }
        catch
        {
            return null;
        }

        string prefix = NormalizeVolumeRoot(volumeRoot);
        string fullTrimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefixTrimmed = prefix.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // путь вне тома (или форма "C:" без слеша)
            return string.Equals(fullTrimmed, prefixTrimmed, StringComparison.OrdinalIgnoreCase)
                ? tree.Root
                : null;
        }

        string remainder = fullPath.Substring(prefix.Length);
        if (string.IsNullOrWhiteSpace(remainder))
            return tree.Root;

        string[] segments = remainder
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(static segment => segment.Length > 0 && segment != "." && segment != "..")
            .ToArray();

        MftTreeNode current = tree.Root;
        foreach (string segment in segments)
        {
            MftTreeNode? next = null;
            foreach (MftTreeNode child in current.Children)
            {
                if (child.IsDirectory
                    && child.FileName != null
                    && string.Equals(child.FileName, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
                return null;
            current = next;
        }

        return current;
    }

    // ---------- внутренняя механика ----------

    private static MftScanResult Failed(MftScanFailureKind kind, string text)
        => new() { Success = false, FailureKind = kind, FailureText = text };

    /// <summary>Предпочтение из единой настройки; отсутствие/битость → Auto.</summary>
    private string LoadPreference()
    {
        try
        {
            return _configService?.Load<ScanSettingsConfig>(SettingsFilename).MftScanMode ?? "Auto";
        }
        catch
        {
            return "Auto";
        }
    }

    private static string NormalizePreference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Auto";
        string trimmed = value.Trim();
        return string.Equals(trimmed, "On", StringComparison.OrdinalIgnoreCase)
            ? "On"
            : string.Equals(trimmed, "Off", StringComparison.OrdinalIgnoreCase)
                ? "Off"
                : "Auto";
    }

    /// <summary>Буква тома из корня пути ("E:\foo" → 'E'); '\0' — путь не распознан.</summary>
    internal static char ExtractDriveLetter(string driveRoot)
    {
        string trimmed = driveRoot.Trim();
        if (trimmed.Length >= 2
            && char.IsAsciiLetterUpper(char.ToUpperInvariant(trimmed[0]))
            && trimmed[1] == ':')
        {
            return char.ToUpperInvariant(trimmed[0]);
        }
        return '\0';
    }

    /// <summary>Счётчик записей конвейера (итераторы не принимают ref, CS1623).</summary>
    private sealed class RecordCounter
    {
        public long Count;
    }

    /// <summary>Считает записи в конвейере перечисления (стриминг, без списка).</summary>
    private static IEnumerable<MftRecord> CountRecords(IEnumerable<MftRecord> source, RecordCounter counter)
    {
        foreach (MftRecord record in source)
        {
            counter.Count++;
            yield return record;
        }
    }

    private static void CollectFiles(
        MftTreeNode node, string directoryPath, long minSize, List<LargeFileInfo> output)
    {
        foreach (MftTreeNode child in node.Children)
        {
            if (child.FileName == null)
                continue; // DOS-only/безымянные записи не попадают в выдачу

            string childPath = Path.Combine(directoryPath, child.FileName);
            if (child.IsDirectory)
            {
                CollectFiles(child, childPath, minSize, output);
            }
            else if (ClampLong(child.Size) >= minSize)
            {
                output.Add(new LargeFileInfo
                {
                    Path = childPath,
                    Name = child.FileName,
                    Directory = directoryPath,
                    Size = ClampLong(child.Size),
                    Modified = default,
                    Extension = Path.GetExtension(child.FileName).ToLowerInvariant()
                });
            }
        }
    }

    private static void CollectFolders(
        MftTreeNode node, string nodePath, string rootPath, long minBytes, List<ScannedItem> output)
    {
        foreach (MftTreeNode child in node.Children)
        {
            if (child.FileName == null || !child.IsDirectory)
                continue;

            string childPath = Path.Combine(nodePath, child.FileName);
            long size = ClampLong(child.Size);
            if (size >= minBytes)
            {
                RiskCategory risk = DiskUsageScanner.ClassifyRisk(childPath, isDirectory: true);
                output.Add(new ScannedItem
                {
                    Path = childPath,
                    Size = size,
                    LastAccess = default,
                    Risk = risk,
                    Description = DiskUsageScanner.BuildDescription(child.FileName, size, risk, isDirectory: true),
                    IsDirectory = true,
                    ParentApp = Localizer.GetString("Scanner_DiskUsage_Root", rootPath),
                    IsSelected = risk == RiskCategory.SafeToDelete
                });
            }

            CollectFolders(child, childPath, rootPath, minBytes, output);
        }
    }

    private static long BuildTreemapChildren(
        MftTreeNode node, string nodePath, int depth, int maxDepth, List<DiskNode> children)
    {
        long total = 0;
        foreach (MftTreeNode child in node.Children)
        {
            if (child.FileName == null)
                continue;

            string childPath = Path.Combine(nodePath, child.FileName);
            if (child.IsDirectory)
            {
                // зеркало BuildNode: на пределе глубины подпапки не добавляются
                if (maxDepth == 0 || depth < maxDepth)
                {
                    var dirNode = new DiskNode
                    {
                        Name = child.FileName,
                        FullPath = childPath,
                        IsFile = false,
                        Depth = depth + 1
                    };
                    dirNode.Size = BuildTreemapChildren(child, childPath, depth + 1, maxDepth, dirNode.Children);
                    children.Add(dirNode);
                    total += dirNode.Size;
                }
            }
            else
            {
                long size = ClampLong(child.Size);
                children.Add(new DiskNode
                {
                    Name = child.FileName,
                    FullPath = childPath,
                    Size = size,
                    IsFile = true,
                    Depth = depth + 1
                });
                total += size;
            }
        }

        children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        return total;
    }

    private static void CalculateTreemapPercentages(DiskNode node)
    {
        if (node.Size == 0)
            return;

        foreach (DiskNode child in node.Children)
        {
            child.Percentage = node.Size > 0
                ? (double)child.Size / node.Size * 100.0
                : 0;
            if (!child.IsFile)
                CalculateTreemapPercentages(child);
        }
    }

    /// <summary>Имя корневого узла: имя записи; "."/".."/null → сегмент корневого пути.</summary>
    private static string ResolveRootName(MftTreeNode subtree, string rootPath)
    {
        string? name = subtree.FileName;
        if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
            return name;

        string trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string segment = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(segment) ? trimmed : segment;
    }

    private static string NormalizeVolumeRoot(string volumeRoot)
    {
        string trimmed = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? volumeRoot : trimmed + Path.DirectorySeparatorChar;
    }

    private static long ClampLong(ulong value)
        => value > long.MaxValue ? long.MaxValue : (long)value;
}
