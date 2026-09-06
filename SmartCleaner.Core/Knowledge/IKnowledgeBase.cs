using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Knowledge;

/// <summary>
/// База знаний о приложениях и их кэше
/// </summary>
public interface IKnowledgeBase
{
    /// <summary>Получить встроенные правила</summary>
    IEnumerable<AppDefinition> GetBuiltInApps();
    
    /// <summary>Получить пользовательские правила</summary>
    IEnumerable<AppDefinition> GetUserApps();
    
    /// <summary>Получить все правила (встроенные + пользовательские)</summary>
    IEnumerable<AppDefinition> GetAllApps();
    
    /// <summary>Добавить пользовательское правило</summary>
    void AddUserApp(AppDefinition app);
    
    /// <summary>Обновить пользовательское правило</summary>
    void UpdateUserApp(AppDefinition app);
    
    /// <summary>Удалить пользовательское правило</summary>
    void RemoveUserApp(string appId);
    
    /// <summary>Обнаружить неизвестные приложения (Auto-Discovery)</summary>
    IEnumerable<DiscoveredApp> DiscoverUnknownApps(string rootPath);
    
    /// <summary>Классифицировать путь</summary>
    PathClassification ClassifyPath(string path);
    
    /// <summary>Загрузить внешние правила из JSON</summary>
    void LoadExternalRules(string jsonPath);
    
    /// <summary>Сохранить пользовательские правила</summary>
    void SaveUserRules();
}
