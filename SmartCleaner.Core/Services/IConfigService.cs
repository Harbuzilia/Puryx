namespace SmartCleaner.Core.Services;

/// <summary>
/// Режим конфигурации приложения
/// </summary>
public enum ConfigMode
{
    /// <summary>Portable — настройки рядом с exe</summary>
    Portable,
    
    /// <summary>Installed — настройки в %APPDATA%</summary>
    Installed
}

/// <summary>
/// Сервис конфигурации — управление настройками и путями
/// </summary>
public interface IConfigService
{
    /// <summary>Текущий режим (Portable или Installed)</summary>
    ConfigMode Mode { get; }
    
    /// <summary>Директория для хранения настроек</summary>
    string ConfigDirectory { get; }
    
    /// <summary>Директория для логов</summary>
    string LogDirectory { get; }
    
    /// <summary>Директория для пользовательских правил</summary>
    string UserRulesDirectory { get; }
    
    /// <summary>Загрузить объект из JSON-файла</summary>
    T Load<T>(string filename) where T : new();
    
    /// <summary>Сохранить объект в JSON-файл</summary>
    void Save<T>(string filename, T data);
    
    /// <summary>Проверить существование файла конфигурации</summary>
    bool Exists(string filename);
}
