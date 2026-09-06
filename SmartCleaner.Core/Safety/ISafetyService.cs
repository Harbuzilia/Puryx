using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Safety;

/// <summary>
/// Сервис безопасности — whitelist, блокировки, защита
/// </summary>
public interface ISafetyService
{
    /// <summary>Период защиты новых файлов (по умолчанию 24 часа)</summary>
    TimeSpan ProtectedPeriod { get; set; }
    
    /// <summary>Проверить, находится ли путь в whitelist</summary>
    bool IsWhitelisted(string path);
    
    /// <summary>Добавить паттерн в whitelist</summary>
    void AddToWhitelist(string pattern);
    
    /// <summary>Удалить паттерн из whitelist</summary>
    void RemoveFromWhitelist(string pattern);
    
    /// <summary>Получить все паттерны whitelist</summary>
    IEnumerable<string> GetWhitelistPatterns();
    
    /// <summary>Проверить, создан ли файл в защищённый период</summary>
    bool IsWithinProtectedPeriod(string path);
    
    /// <summary>Проверить, заблокирован ли файл процессом</summary>
    bool IsFileLocked(string path);
    
    /// <summary>Валидация перед удалением</summary>
    DeleteValidation ValidateForDeletion(ScannedItem item);
    
    /// <summary>Сохранить настройки whitelist</summary>
    void SaveWhitelist();
    
    /// <summary>Загрузить настройки whitelist</summary>
    void LoadWhitelist();
}
