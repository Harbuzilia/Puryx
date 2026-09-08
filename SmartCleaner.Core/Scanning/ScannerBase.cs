using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Knowledge;
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
    protected readonly IKnowledgeBase _knowledge;
    protected readonly ISafetyService _safety;
    protected readonly ILogger _logger;
    
    public abstract string CategoryName { get; }
    public abstract string CategoryIcon { get; }
    public abstract int DisplayOrder { get; }
    public virtual bool IsEnabledByDefault => true;

    protected ScannerBase(IKnowledgeBase knowledge, ISafetyService safety, ILogger? logger = null)
    {
        _knowledge = knowledge;
        _safety = safety;
        _logger = logger ?? NullLogger.Instance;
    }

    public abstract Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Рассчитать размер директории рекурсивно
    /// </summary>
    protected long CalculateDirectorySize(string path)
    {
        if (!Directory.Exists(path))
            return 0;

        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(file => 
                {
                    try { return file.Length; }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScannerBase] File size error: {ex.Message}"); return 0; }
                });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ScannerBase] CalculateDirectorySize error: {ex.Message}"); return 0; }
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
