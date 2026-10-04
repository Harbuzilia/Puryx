using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartCleaner.Core.Scanning;

/// <summary>
/// Базовый класс для всех сканеров с общей логикой
/// </summary>
public abstract class ScannerBase : IScannerStrategy
{
    protected readonly ISafetyService _safety;
    protected readonly ILogger _logger;

    public abstract string CategoryName { get; }
    public abstract string CategoryIcon { get; }
    public abstract int DisplayOrder { get; }
    public virtual bool IsEnabledByDefault => true;

    protected ScannerBase(ISafetyService safety, ILogger? logger = null)
    {
        _safety = safety;
        _logger = logger ?? NullLogger.Instance;
    }

    public abstract Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Рассчитать размер директории рекурсивно.
    /// Перечисление с цикл-гвардом: reparse-точки не рекурсируются —
    /// циклический junction не вешает подсчёт, содержимое за junction
    /// в размер не входит (findings M1, День 14).
    /// Работает на FileSystemInfo из перечисления: атрибуты и Length берутся
    /// из find-данных без stat-системного вызова на каждый файл.
    /// </summary>
    protected long CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        try
        {
            return SumDirectorySize(new DirectoryInfo(path));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScannerBase] CalculateDirectorySize error: {ex.Message}"); return 0; }
    }

    /// <summary>Рекурсивная сумма размеров файлов с цикл-гвардом reparse-точек.</summary>
    private long SumDirectorySize(DirectoryInfo directory)
    {
        long total = 0;

        IEnumerable<FileSystemInfo> entries;
        try
        {
            // Однопроходное перечисление (файлы и каталоги одним дескриптором).
            entries = directory.EnumerateFileSystemInfos();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScannerBase] Enumerate entries error: {ex.Message}");
            return 0;
        }

        foreach (var entry in entries)
        {
            if (entry is FileInfo file)
            {
                try { total += file.Length; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScannerBase] File size error: {ex.Message}"); }
            }
            else if (entry is DirectoryInfo subDirectory)
            {
                // Цикл-гвард: спуск в reparse-точку запрещён (атрибут — из find-данных).
                bool isReparse;
                try { isReparse = (subDirectory.Attributes & FileAttributes.ReparsePoint) != 0; }
                catch { isReparse = true; } // непроверяемый каталог не рекурсируем (fail-closed)

                if (!isReparse)
                {
                    total += SumDirectorySize(subDirectory);
                }
            }
        }

        return total;
    }

    /// <summary>
    /// Безопасно перечислить подпапки в директории, логируя ошибки доступа
    /// вместо молчаливого проглатывания. Используйте в ScanAsync сканеров.
    /// </summary>
    /// <param name="path">Путь к родительской директории</param>
    /// <param name="searchPattern">Паттерн поиска (по умолчанию "*")</param>
    /// <returns>Перечисление найденных директорий (пропуская недоступные)</returns>
    protected IEnumerable<string> SafeEnumerateDirectories(string path, string searchPattern = "*")
    {
        if (!Directory.Exists(path))
            yield break;

        IEnumerator<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateDirectories(path, searchPattern).GetEnumerator();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("[{Category}] Нет доступа к '{Path}': {Error}", CategoryName, path, ex.Message);
            yield break;
        }
        catch (IOException ex)
        {
            _logger.LogWarning("[{Category}] Ошибка I/O при чтении '{Path}': {Error}", CategoryName, path, ex.Message);
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                        break;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("[{Category}] Пропущена подпапка в '{Path}': {Error}", CategoryName, path, ex.Message);
                    continue;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning("[{Category}] Ошибка I/O: {Error}", CategoryName, ex.Message);
                    continue;
                }

                yield return enumerator.Current;
            }
        }
    }

    /// <summary>
    /// Безопасно перечислить файлы, логируя ошибки доступа
    /// </summary>
    protected IEnumerable<string> SafeEnumerateFiles(string path, string searchPattern = "*", SearchOption option = SearchOption.TopDirectoryOnly)
    {
        if (!Directory.Exists(path))
            yield break;

        IEnumerator<string> enumerator;
        try
        {
            enumerator = Directory.EnumerateFiles(path, searchPattern, option).GetEnumerator();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("[{Category}] Нет доступа к файлам в '{Path}': {Error}", CategoryName, path, ex.Message);
            yield break;
        }
        catch (IOException ex)
        {
            _logger.LogWarning("[{Category}] Ошибка I/O при чтении файлов '{Path}': {Error}", CategoryName, path, ex.Message);
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                try
                {
                    if (!enumerator.MoveNext())
                        break;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning("[{Category}] Пропущен файл в '{Path}': {Error}", CategoryName, path, ex.Message);
                    continue;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning("[{Category}] Ошибка I/O: {Error}", CategoryName, ex.Message);
                    continue;
                }

                yield return enumerator.Current;
            }
        }
    }

    /// <summary>
    /// Рекурсивно перечисляет файлы с цикл-гвардом: reparse-точки (junction/symlink)
    /// НЕ рекурсируются. BCL SearchOption.AllDirectories следует за junction —
    /// циклический junction даёт неограниченный спуск до исчерпания длины пути
    /// (findings M1, День 14); здесь спуск останавливается на reparse-точке,
    /// поэтому перечисление гарантированно завершается. Ошибки доступа
    /// логируются и каталог пропускается.
    /// </summary>
    /// <param name="path">Корневой каталог (сам может быть junction — его верхний уровень перечисляется)</param>
    /// <param name="searchPattern">Паттерн поиска (по умолчанию "*")</param>
    protected IEnumerable<string> SafeEnumerateFilesRecursive(string path, string searchPattern = "*")
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var file in SafeEnumerateFiles(current, searchPattern))
            {
                yield return file;
            }

            foreach (var subDirectory in SafeEnumerateDirectories(current))
            {
                if (!IsReparseDirectory(subDirectory))
                {
                    pending.Push(subDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Рекурсивно перечисляет каталоги с цикл-гвардом: reparse-точки
    /// (junction/symlink) НЕ рекурсируются (findings M1, День 14), но сами
    /// могут выдаваться в выдачу, если совпадают с паттерном. Ошибки доступа
    /// логируются и каталог пропускается.
    /// </summary>
    /// <param name="path">Корневой каталог</param>
    /// <param name="searchPattern">Паттерн имени каталога (по умолчанию "*")</param>
    protected IEnumerable<string> SafeEnumerateDirectoriesRecursive(string path, string searchPattern = "*")
    {
        if (!Directory.Exists(path))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var directory in SafeEnumerateDirectories(current, searchPattern))
            {
                yield return directory;
            }

            foreach (var subDirectory in SafeEnumerateDirectories(current))
            {
                if (!IsReparseDirectory(subDirectory))
                {
                    pending.Push(subDirectory);
                }
            }
        }
    }

    /// <summary>
    /// Каталог — reparse-точка (junction/symlink): рекурсия в него запрещена
    /// (цикл-гвард перечислений). Ошибка получения атрибутов трактуется как
    /// reparse — непроверяемый каталог не рекурсируется (fail-closed).
    /// </summary>
    protected static bool IsReparseDirectory(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Гейт выдачи сканера (findings M1, День 14): путь item резолвится в
    /// реальный (PathResolver.ResolveRealPath); защищённые по реальному пути
    /// цели (whitelist) не попадают в выдачу — иначе junction в сканируемой
    /// зоне отдаёт файлы из защищённых зон под видом чистимого кэша.
    /// Неразрешимая reparse-цепочка (Resolved=false) — fail-closed: item
    /// не выдаётся.
    /// </summary>
    protected bool IsProtectedScanTarget(string path)
    {
        var resolved = PathResolver.ResolveRealPath(path);
        return !resolved.Resolved || _safety.IsWhitelisted(resolved.Path);
    }

    /// <summary>
    /// Получить размер файла безопасно
    /// </summary>
    protected long GetFileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Проверить, заблокирован ли файл
    /// </summary>
    protected bool IsFileLocked(string path)
    {
        return _safety.IsFileLocked(path);
    }

    /// <summary>
    /// Проверить, создан ли файл в последние 24 часа
    /// </summary>
    protected bool IsWithin24Hours(string path)
    {
        return _safety.IsWithinProtectedPeriod(path);
    }

    /// <summary>
    /// Развернуть переменные окружения в пути
    /// </summary>
    protected string ExpandPath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>
    /// Проверить существование пути (файл или папка)
    /// </summary>
    protected bool PathExists(string path)
    {
        var expanded = ExpandPath(path);
        return Directory.Exists(expanded) || File.Exists(expanded);
    }

    /// <summary>
    /// Получить дату последнего доступа
    /// </summary>
    protected DateTime GetLastAccess(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return Directory.GetLastAccessTime(path);
            if (File.Exists(path))
                return File.GetLastAccessTime(path);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScannerBase] GetLastAccess error: {ex.Message}"); }
        
        return DateTime.MinValue;
    }

    /// <summary>
    /// Создать ScannedItem для директории.
    /// Поддерживает все поля: description, explanation, howToRestore, restoreCommand.
    /// </summary>
    protected ScannedItem CreateDirectoryItem(
        string path,
        RiskCategory risk,
        string description,
        string? parentApp = null,
        string? restoreCommand = null,
        string? explanation = null,
        string? howToRestore = null)
    {
        // Баг 7: Не проверяем locked при сканировании — SafetyService проверит перед удалением.
        // Между сканом и удалением может пройти время, и файл разблокируется.
        return new ScannedItem
        {
            Path = path,
            Size = CalculateDirectorySize(path),
            LastAccess = GetLastAccess(path),
            Risk = risk,
            Description = description,
            Explanation = explanation,
            HowToRestore = howToRestore,
            IsLocked = false,
            RestoreCommand = restoreCommand,
            IsDirectory = true,
            ParentApp = parentApp,
            IsSelected = risk == RiskCategory.SafeToDelete
        };
    }

    /// <summary>
    /// Создать ScannedItem для файла.
    /// Поддерживает все поля: description, explanation, howToRestore.
    /// </summary>
    protected ScannedItem CreateFileItem(
        string path,
        RiskCategory risk,
        string description,
        string? parentApp = null,
        string? explanation = null,
        string? howToRestore = null)
    {
        // Баг 7: Не проверяем locked при сканировании — SafetyService проверит перед удалением.
        return new ScannedItem
        {
            Path = path,
            Size = GetFileSize(path),
            LastAccess = GetLastAccess(path),
            Risk = risk,
            Description = description,
            Explanation = explanation,
            HowToRestore = howToRestore,
            IsLocked = false,
            IsDirectory = false,
            ParentApp = parentApp,
            IsSelected = risk == RiskCategory.SafeToDelete
        };
    }

    /// <summary>
    /// Форматировать размер для отображения
    /// </summary>
    protected static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}
