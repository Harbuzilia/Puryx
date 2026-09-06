namespace SmartCleaner.Core.DiskMap;

/// <summary>
/// Узел дерева файловой системы для TreeMap.
/// </summary>
public sealed class DiskNode
{
    /// <summary>Имя файла или папки.</summary>
    public string Name { get; set; } = "";

    /// <summary>Полный путь.</summary>
    public string FullPath { get; set; } = "";

    /// <summary>Размер в байтах (для папок — сумма вложенных).</summary>
    public long Size { get; set; }

    /// <summary>true = файл, false = папка.</summary>
    public bool IsFile { get; set; }

    /// <summary>Дочерние узлы (для папок).</summary>
    public List<DiskNode> Children { get; set; } = [];

    /// <summary>Глубина от корня.</summary>
    public int Depth { get; set; }

    /// <summary>Процент от родителя.</summary>
    public double Percentage { get; set; }
}
