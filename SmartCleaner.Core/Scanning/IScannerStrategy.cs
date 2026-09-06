using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Scanning;

/// <summary>
/// Стратегия сканирования для определённой категории
/// </summary>
public interface IScannerStrategy
{
    /// <summary>Название категории (для UI)</summary>
    string CategoryName { get; }
    
    /// <summary>Иконка категории (Segoe MDL2 Assets)</summary>
    string CategoryIcon { get; }
    
    /// <summary>Порядок отображения в UI</summary>
    int DisplayOrder { get; }
    
    /// <summary>Включён ли сканер по умолчанию</summary>
    bool IsEnabledByDefault { get; }
    
    /// <summary>
    /// Выполнить сканирование
    /// </summary>
    /// <param name="progress">Прогресс для UI</param>
    /// <param name="ct">Токен отмены</param>
    /// <returns>Результат сканирования</returns>
    Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default);
}
