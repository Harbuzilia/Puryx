namespace SmartCleaner.Core.Models;

/// <summary>
/// Определение приложения в базе знаний
/// </summary>
public record AppDefinition
{
    /// <summary>Уникальный идентификатор</summary>
    public required string Id { get; init; }
    
    /// <summary>Отображаемое имя</summary>
    public required string DisplayName { get; init; }
    
    /// <summary>Корневые пути приложения (поддерживают переменные окружения)</summary>
    public IReadOnlyList<string> RootPaths { get; init; } = [];
    
    /// <summary>Паттерны для кэша (glob-формат, относительно RootPaths)</summary>
    public IReadOnlyList<string> CachePatterns { get; init; } = [];
    
    /// <summary>Защищённые паттерны — НИКОГДА не удалять</summary>
    public IReadOnlyList<string> ProtectedPatterns { get; init; } = [];
    
    /// <summary>Маркеры в workspace (например, .agent, .kiro)</summary>
    public IReadOnlyList<string> WorkspaceMarkers { get; init; } = [];
    
    /// <summary>Описание для пользователя</summary>
    public string? Description { get; init; }
    
    /// <summary>Пользовательское правило (не встроенное)</summary>
    public bool IsUserDefined { get; init; }
}

/// <summary>
/// Обнаруженное приложение (Auto-Discovery)
/// </summary>
public record DiscoveredApp
{
    public required string Path { get; init; }
    public required string SuggestedName { get; init; }
    public IReadOnlyList<string> SuggestedCacheFolders { get; init; } = [];
    public IReadOnlyList<string> SuggestedProtectedFiles { get; init; } = [];
    public long TotalSize { get; init; }
}

/// <summary>
/// Классификация пути
/// </summary>
public record PathClassification
{
    public bool IsKnownApp { get; init; }
    public string? AppName { get; init; }
    public RiskCategory SuggestedRisk { get; init; }
    public required string Reason { get; init; }
}
