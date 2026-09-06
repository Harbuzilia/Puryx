namespace SmartCleaner.Core.Duplicates;

/// <summary>
/// Файл, найденный при сканировании дубликатов.
/// </summary>
public sealed class DuplicateFile
{
    /// <summary>Полный путь к файлу.</summary>
    public required string Path { get; init; }

    /// <summary>Имя файла без пути.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>Размер файла в байтах.</summary>
    public long Size { get; init; }

    /// <summary>Дата последнего изменения.</summary>
    public DateTime Modified { get; init; }

    /// <summary>Хэш файла (SHA256 или turbo). Null до вычисления.</summary>
    public string? Hash { get; set; }
}

/// <summary>
/// Группа дубликатов — файлы с одинаковым ключом (размер + хэш/имя).
/// </summary>
public sealed class DuplicateGroup
{
    /// <summary>Уникальный ключ группы (например "size|hash").</summary>
    public required string Key { get; init; }

    /// <summary>Файлы в группе (≥2 штуки).</summary>
    public required List<DuplicateFile> Files { get; init; }

    /// <summary>Потраченное место: (Count - 1) × Size.</summary>
    public long WastedBytes => Files.Count > 1 ? (Files.Count - 1) * Files[0].Size : 0;
}

/// <summary>
/// Параметры поиска дубликатов.
/// </summary>
public sealed class DuplicateScanOptions
{
    /// <summary>Сравнивать по имени файла.</summary>
    public bool ByName { get; set; }

    /// <summary>Сравнивать по размеру (первичная группировка).</summary>
    public bool BySize { get; set; } = true;

    /// <summary>Сравнивать по хэшу содержимого.</summary>
    public bool ByHash { get; set; } = true;

    /// <summary>Побайтовая верификация (максимальная точность).</summary>
    public bool ByByte { get; set; }

    /// <summary>Turbo mode — хэш только первых+последних 64KB.</summary>
    public bool TurboMode { get; set; }

    /// <summary>Паттерны исключений (glob): ".git", "node_modules", "*.tmp".</summary>
    public List<string> ExcludePatterns { get; set; } = [];

    /// <summary>Минимальный размер файла (байт). 0 = без ограничения.</summary>
    public long MinFileSize { get; set; } = 1;
}

/// <summary>
/// Результат сравнения двух папок.
/// </summary>
public sealed class FolderCompareResult
{
    /// <summary>Файлы, существующие только в папке A.</summary>
    public required List<DuplicateFile> UniqueA { get; init; }

    /// <summary>Файлы, существующие только в папке B.</summary>
    public required List<DuplicateFile> UniqueB { get; init; }

    /// <summary>Общие файлы (с анализом различий).</summary>
    public required List<CommonFile> Common { get; init; }

    /// <summary>Общее количество уникальных относительных путей.</summary>
    public int TotalPaths => UniqueA.Count + UniqueB.Count + Common.Count;
}

/// <summary>
/// Файл, присутствующий в обеих сравниваемых папках.
/// </summary>
public sealed class CommonFile
{
    /// <summary>Относительный путь от корня папки.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Файл из папки A.</summary>
    public required DuplicateFile FileA { get; init; }

    /// <summary>Файл из папки B.</summary>
    public required DuplicateFile FileB { get; init; }

    /// <summary>Схожесть: 1.0 = идентичны, 0.0 = полностью различны.</summary>
    public double Similarity { get; init; }

    /// <summary>"A" если A новее, "B" если B новее, "same" если одинаково.</summary>
    public string Newer { get; init; } = "same";

    /// <summary>Размеры одинаковы?</summary>
    public bool SameSize => FileA.Size == FileB.Size;
}
