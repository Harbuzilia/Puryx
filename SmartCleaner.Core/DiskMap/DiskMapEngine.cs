using SmartCleaner.Core.Helpers;

namespace SmartCleaner.Core.DiskMap;

/// <summary>
/// Сканирует структуру каталогов для построения карты диска (TreeMap).
/// Рекурсивно обходит файловую систему, собирая размеры.
/// </summary>
public sealed class DiskMapEngine
{
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
        progress?.Report($"Сканирование: {rootPath}");

        var root = await Task.Run(() => BuildNode(rootPath, 0, maxDepth, progress, ct), ct);

        // Вычисляем проценты
        CalculatePercentages(root);

        progress?.Report($"Готово. {SizeFormatter.Format(root.Size)} в {root.FullPath}");
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
                            progress?.Report($"Сканирование: {subDir.Name} ({SizeFormatter.Format(child.Size)})");
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
