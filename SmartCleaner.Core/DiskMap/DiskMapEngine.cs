using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Mft;

namespace SmartCleaner.Core.DiskMap;

/// <summary>
/// Сканирует структуру каталогов для построения карты диска (TreeMap).
/// Рекурсивно обходит файловую систему, собирая размеры.
/// </summary>
public sealed class DiskMapEngine
{
    private readonly MftDiskScanner? _mftScanner;

    /// <summary>
    /// DI-конструктор (День 9, ROADMAP v3.1, фаза M): MFT-путь включается
    /// передачей фасада; null — прежний directory-walk.
    /// </summary>
    public DiskMapEngine(MftDiskScanner? mftScanner = null)
    {
        _mftScanner = mftScanner;
    }

    /// <summary>
    /// Сканирует указанную директорию и строит дерево узлов.
    /// </summary>
    /// <param name="rootPath">Корневая директория.</param>
    /// <param name="maxDepth">Максимальная глубина сканирования (0 = без ограничений).</param>
    /// <param name="progress">Текстовый прогресс.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>Корневой узел дерева.</returns>
    public async Task<DiskNode> ScanAsync(
        string rootPath,
        int maxDepth = 5,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(Localizer.GetString("DiskMap_Progress_Scanning", rootPath));

        // День 9 (ROADMAP v3.1, фаза M): MFT-путь — том одним проходом $MFT,
        // дерево узлов строится конверсией ToTreemapInput; отказ → честный
        // фолбэк на обход с причиной в progress (ключи Mft_Status_Mode_*)
        var root = await Task.Run(() =>
        {
            if (_mftScanner is not null)
            {
                MftScanResult volumeScan = _mftScanner.TryScanDrive(rootPath, ct);
                if (volumeScan.Success && volumeScan.Tree is not null && volumeScan.Tree.Root is not null)
                {
                    char letter = MftDiskScanner.ExtractDriveLetter(rootPath);
                    MftTreeNode? subtree = letter != '\0'
                        ? MftDiskScanner.FindSubtree(volumeScan.Tree, $"{letter}:\\", rootPath)
                        : null;
                    if (subtree is not null)
                    {
                        progress?.Report(Localizer.GetString("Mft_Status_Mode_Mft"));
                        return MftDiskScanner.ToTreemapInput(subtree, rootPath, maxDepth);
                    }

                    progress?.Report(Localizer.GetString(
                        "Mft_Status_Mode_Walk", Localizer.GetString("Mft_Fallback_PathNotFound")));
                }
                else
                {
                    progress?.Report(Localizer.GetString("Mft_Status_Mode_Walk", volumeScan.FailureText ?? ""));
                }
            }

            return BuildNode(rootPath, 0, maxDepth, progress, ct);
        }, ct);

        // Вычисляем проценты
        CalculatePercentages(root);

        progress?.Report(Localizer.GetString("DiskMap_Progress_Done", SizeFormatter.Format(root.Size), root.FullPath));
        return root;
    }

    /// <summary>
    /// Рекурсивно строит дерево узлов файловой системы.
    /// </summary>
    private static DiskNode BuildNode(
        string path, int depth, int maxDepth,
        IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var di = new DirectoryInfo(path);
        var node = new DiskNode
        {
            Name = di.Name,
            FullPath = di.FullName,
            IsFile = false,
            Depth = depth
        };

        try
        {
            // Файлы текущего уровня
            foreach (var fi in di.EnumerateFiles())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    node.Children.Add(new DiskNode
                    {
                        Name = fi.Name,
                        FullPath = fi.FullName,
                        Size = fi.Length,
                        IsFile = true,
                        Depth = depth + 1
                    });
                    node.Size += fi.Length;
                }
                catch { /* пропускаем недоступные файлы */ }
            }

            // Подкаталоги
            if (maxDepth == 0 || depth < maxDepth)
            {
                foreach (var subDir in di.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue; // пропускаем символические ссылки

                        var child = BuildNode(subDir.FullName, depth + 1, maxDepth, progress, ct);
                        node.Children.Add(child);
                        node.Size += child.Size;

                        if (depth == 0)
                            progress?.Report(Localizer.GetString("DiskMap_Progress_Subdirectory", subDir.Name, SizeFormatter.Format(child.Size)));
                    }
                    catch { /* пропускаем недоступные подкаталоги */ }
                }
            }
        }
        catch { /* пропускаем корневую ошибку доступа */ }

        // Сортируем по размеру (большие сверху)
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));

        return node;
    }

    /// <summary>
    /// Вычисляет процент от родителя для каждого узла.
    /// </summary>
    private static void CalculatePercentages(DiskNode node)
    {
        if (node.Size == 0) return;

        foreach (var child in node.Children)
        {
            child.Percentage = node.Size > 0
                ? (double)child.Size / node.Size * 100.0
                : 0;
            if (!child.IsFile)
                CalculatePercentages(child);
        }
    }
}
