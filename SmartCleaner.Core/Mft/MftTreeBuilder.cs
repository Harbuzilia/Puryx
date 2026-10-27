namespace SmartCleaner.Core.Mft;

/// <summary>
/// Узел дерева файлов тома (День 8, ROADMAP v3.1): запись MFT + позиция в
/// иерархии + итоговый размер. Жёсткие связи: один inode = одна запись =
/// один узел (ExtraLinks &gt; 0), размер НЕ размножается — дерево отражает
/// структуру каталогов, а не число ссылок.
/// </summary>
public sealed class MftTreeNode
{
    private readonly List<MftTreeNode> _children = [];

    internal MftTreeNode(MftRecord record)
    {
        RecordNumber = record.Number;
        FileName = record.BestFileName;
        IsDirectory = (record.Flags & MftRecordFlags.Directory) != 0;
        Sequence = record.Sequence;
        AllocatedSize = record.AllocatedSize;
        RealSize = record.RealSize;
        ExtraLinks = record.ExtraLinks;
    }

    /// <summary>Номер записи (позиция слота в $MFT, уникален).</summary>
    public ulong RecordNumber { get; }

    /// <summary>Лучшее имя записи; null для записей без не-DOS имени.</summary>
    public string? FileName { get; }

    /// <summary>Запись описывает каталог.</summary>
    public bool IsDirectory { get; }

    /// <summary>Sequence записи — сверяется с родительской ссылкой детей.</summary>
    public ushort Sequence { get; }

    /// <summary>Выделенный размер данных (кластеры неименованного $DATA; слитый с extension).</summary>
    public ulong AllocatedSize { get; }

    /// <summary>Реальный размер данных (слитый с extension).</summary>
    public ulong RealSize { get; }

    /// <summary>Жёсткие связи сверх канонической (linkCount − 1).</summary>
    public int ExtraLinks { get; }

    /// <summary>
    /// Итоговый размер: файл — AllocatedSize &gt; 0 ? AllocatedSize : RealSize
    /// (резидентные данные не занимают кластеров); каталог — Σ размеров
    /// детей (снизу вверх; узлы в циклах получают частичную сумму).
    /// </summary>
    public ulong Size { get; internal set; }

    /// <summary>Родитель; null для корня и верхних узлов леса сирот.</summary>
    public MftTreeNode? Parent { get; internal set; }

    /// <summary>Дети в порядке привязки; для сирот — поддерево леса сирот.</summary>
    public IReadOnlyList<MftTreeNode> Children => _children;

    internal void AddChild(MftTreeNode child)
    {
        _children.Add(child);
        child.Parent = this;
    }

    /// <summary>Вычисляет итоговый размер (вызывается пост-порядком при построении).</summary>
    internal void FinalizeSize()
    {
        if (!IsDirectory)
        {
            Size = AllocatedSize > 0 ? AllocatedSize : RealSize;
            return;
        }

        ulong sum = 0;
        foreach (MftTreeNode child in _children)
            sum += child.Size; // невычисленные дети (цикл) дают 0
        Size = sum;
    }
}

/// <summary>
/// Дерево файлов тома: корень (запись 5), все узлы по номерам записей и
/// лес сирот — записи, не дотянутые до корня (родителя нет, родитель
/// переработан, цикл ссылок, extension без базовой). Ничто из валидного
/// входа не теряется: сироты доступны списком и словарём.
/// </summary>
public sealed class MftTree
{
    internal MftTree(MftTreeNode? root, IReadOnlyDictionary<ulong, MftTreeNode> nodes, IReadOnlyList<MftTreeNode> orphans)
    {
        Root = root;
        Nodes = nodes;
        Orphans = orphans;
    }

    /// <summary>Корень тома (запись 5, каталог «.»); null, если записи нет во входе.</summary>
    public MftTreeNode? Root { get; }

    /// <summary>Все узлы по номеру записи (включая сирот).</summary>
    public IReadOnlyDictionary<ulong, MftTreeNode> Nodes { get; }

    /// <summary>Узлы, недостижимые из корня (полный список, включая их поддеревья).</summary>
    public IReadOnlyList<MftTreeNode> Orphans { get; }
}

/// <summary>
/// Сборка дерева файлов из MFT-записей (День 8): слияние extension-записей
/// до построения (размеры суммируются), привязка детей по родителю со
/// сверкой sequence (мисматч = переработанный родитель → сирота),
/// достижимость DFS от корня (циклы не зацикливают — guard), итоговые
/// размеры снизу вверх без рекурсии. Записи с ошибками (удалённые,
/// битые, torn) в дерево не попадают.
/// </summary>
public static class MftTreeBuilder
{
    private const ulong RootRecordNumber = 5;

    /// <summary>Строит дерево по потоку записей; порядок входа произвольный.</summary>
    /// <param name="records">Записи тома (например, из <see cref="IMftVolumeReader.EnumerateRecords"/>).</param>
    public static MftTree Build(IEnumerable<MftRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        // 1) Живые записи; базовые — первое вхождение номера (жёсткие связи
        //    не размножают узлы: один inode = одна запись), extension — отдельно
        var baseRecords = new Dictionary<ulong, MftRecord>();
        var extensions = new List<MftRecord>();
        foreach (MftRecord record in records)
        {
            if (record.Error != null)
                continue; // NotARecord/Corrupt/TornRead/Malformed/Deleted — вне дерева
            if (record.BaseRecordNumber != null)
                extensions.Add(record);
            else if (!baseRecords.ContainsKey(record.Number))
                baseRecords.Add(record.Number, record);
        }

        // 2) Слияние extension до построения: размеры суммируются, узла-extension нет
        foreach (MftRecord extension in extensions)
        {
            ulong baseNumber = extension.BaseRecordNumber!.Value;
            if (baseRecords.TryGetValue(baseNumber, out MftRecord @base))
            {
                baseRecords[baseNumber] = @base.WithSizes(
                    @base.AllocatedSize + extension.AllocatedSize,
                    @base.RealSize + extension.RealSize);
            }
            else if (!baseRecords.ContainsKey(extension.Number))
            {
                // extension без базовой во входе — узел-сирота (данные не терять)
                baseRecords.Add(extension.Number, extension);
            }
        }

        // 3) Узлы
        var nodes = new Dictionary<ulong, MftTreeNode>(baseRecords.Count);
        foreach (KeyValuePair<ulong, MftRecord> pair in baseRecords)
            nodes.Add(pair.Key, new MftTreeNode(pair.Value));

        // 4) Привязка детей по родителю со сверкой sequence родителя
        foreach (KeyValuePair<ulong, MftRecord> pair in baseRecords)
        {
            MftRecord record = pair.Value;
            MftTreeNode node = nodes[pair.Key];
            if (node.RecordNumber == RootRecordNumber)
                continue; // корень не привязывается к родителю
            if (record.ParentRecordNumber is not ulong parentNumber)
                continue; // без родителя — уйдёт в сироты через недостижимость
            if (!nodes.TryGetValue(parentNumber, out MftTreeNode? parent))
                continue; // родителя нет — сирота
            if (parent == node)
                continue; // ссылка на себя — цикл
            if (record.ParentSequence is ushort parentSequence && parentSequence != parent.Sequence)
                continue; // родитель переработан — ребёнок сирота

            parent.AddChild(node);
        }

        // 5) Достижимость от корня: итеративный DFS (guard от циклов)
        MftTreeNode? root = nodes.TryGetValue(RootRecordNumber, out MftTreeNode? rootNode)
            ? rootNode
            : null;
        var reachable = new HashSet<ulong>();
        if (root != null)
        {
            reachable.Add(root.RecordNumber);
            var stack = new Stack<MftTreeNode>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                MftTreeNode current = stack.Pop();
                foreach (MftTreeNode child in current.Children)
                {
                    if (reachable.Add(child.RecordNumber))
                        stack.Push(child);
                }
            }
        }

        // 6) Сироты: всё недостижимое (включая поддеревья и чистые циклы)
        var orphans = new List<MftTreeNode>();
        foreach (KeyValuePair<ulong, MftTreeNode> pair in nodes)
        {
            if (!reachable.Contains(pair.Key))
                orphans.Add(pair.Value);
        }

        // 7) Итоговые размеры снизу вверх для всех узлов
        ComputeSizes(nodes);

        return new MftTree(root, nodes, orphans);
    }

    /// <summary>
    /// Итеративный пост-порядок: размер узла вычисляется после детей
    /// (общие visited-множества, без рекурсии); узлы в циклах получают
    /// частичные суммы, алгоритм не зацикливается.
    /// </summary>
    private static void ComputeSizes(Dictionary<ulong, MftTreeNode> nodes)
    {
        var computed = new HashSet<ulong>();
        foreach (KeyValuePair<ulong, MftTreeNode> pair in nodes)
        {
            if (computed.Contains(pair.Key))
                continue;

            var stack = new Stack<(MftTreeNode Node, int NextChild)>();
            var path = new HashSet<ulong>();
            stack.Push((pair.Value, 0));
            path.Add(pair.Key);
            while (stack.Count > 0)
            {
                (MftTreeNode node, int nextChild) = stack.Pop();
                if (nextChild < node.Children.Count)
                {
                    MftTreeNode child = node.Children[nextChild];
                    stack.Push((node, nextChild + 1));
                    if (computed.Contains(child.RecordNumber))
                        continue;
                    if (path.Contains(child.RecordNumber))
                        continue; // цикл — не спускаемся
                    path.Add(child.RecordNumber);
                    stack.Push((child, 0));
                }
                else
                {
                    node.FinalizeSize();
                    computed.Add(node.RecordNumber);
                    path.Remove(node.RecordNumber);
                }
            }
        }
    }
}
