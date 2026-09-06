using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

/// <summary>
/// Сервис очистки файлов
/// </summary>
public interface ICleaningService
{
    /// <summary>Режим удаления (в корзину или навсегда)</summary>
    CleaningMode Mode { get; set; }
    
    /// <summary>Событие завершения очистки</summary>
    event EventHandler<CleaningResult>? CleaningCompleted;
    
    /// <summary>
    /// Выполнить очистку выбранных элементов
    /// </summary>
    Task<CleaningResult> CleanAsync(
        IEnumerable<ScannedItem> items,
        IProgress<CleaningProgress>? progress = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Выполнить очистку с повышенными правами (для системных папок)
    /// </summary>
    Task<CleaningResult> CleanElevatedAsync(
        IEnumerable<ScannedItem> items,
        CancellationToken ct = default);
    
    /// <summary>
    /// Проверить, требуются ли повышенные права для списка элементов
    /// </summary>
    bool RequiresElevation(IEnumerable<ScannedItem> items);
}
