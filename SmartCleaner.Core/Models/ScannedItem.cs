namespace SmartCleaner.Core.Models;

/// <summary>
/// Категория риска для найденного элемента
/// </summary>
public enum RiskCategory
{
    /// <summary>Зелёный — безопасно удалять (мусор, временные файлы)</summary>
    SafeToDelete,
    
    /// <summary>Жёлтый — кэш, замедлит следующий запуск приложения</summary>
    PerformanceCache,
    
    /// <summary>Красный — пользовательские данные, НЕЛЬЗЯ удалять</summary>
    UserData,
    
    /// <summary>Серый — файл заблокирован процессом</summary>
    Locked
}

/// <summary>
/// Найденный элемент для очистки
/// </summary>
public record ScannedItem
{
    /// <summary>Полный путь к файлу/папке</summary>
    public required string Path { get; init; }
    
    /// <summary>Размер в байтах</summary>
    public long Size { get; init; }
    
    /// <summary>Дата последнего доступа</summary>
    public DateTime LastAccess { get; init; }
    
    /// <summary>Категория риска</summary>
    public RiskCategory Risk { get; init; }
    
    /// <summary>Описание для пользователя (краткое)</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Развёрнутое объяснение: что это за папка/файл, зачем нужна, почему занимает место.
    /// Решает проблему «не знаю, что это и за что отвечает».
    /// </summary>
    public string? Explanation { get; init; }

    /// <summary>
    /// Как восстановить данные если удалил. Например: «npm install скачает пакеты заново».
    /// </summary>
    public string? HowToRestore { get; init; }
    
    /// <summary>Заблокирован ли файл процессом</summary>
    public bool IsLocked { get; init; }
    
    /// <summary>Команда для восстановления (например, "npm install")</summary>
    public string? RestoreCommand { get; init; }
    
    /// <summary>Это директория (true) или файл (false)</summary>
    public bool IsDirectory { get; init; }
    
    /// <summary>Имя родительского приложения</summary>
    public string? ParentApp { get; init; }
    
    /// <summary>Выбран ли элемент для удаления</summary>
    public bool IsSelected { get; set; }

    /// <summary>Тип операции обслуживания для элемента</summary>
    public CleaningActionTarget ActionTarget { get; init; } = CleaningActionTarget.FileSystem;

    /// <summary>Пакетный менеджер для package-действий</summary>
    public PackageManagerType? PackageManager { get; init; }

    /// <summary>Имя пакета для package-действий</summary>
    public string? PackageName { get; init; }

    /// <summary>Рабочая директория для package-действий</summary>
    public string? PackageWorkingDirectory { get; init; }

    /// <summary>Явный путь к исполняемому файлу менеджера пакетов</summary>
    public string? PackageExecutablePath { get; init; }

    /// <summary>Readonly-инвентарь, по умолчанию не допускает действие</summary>
    public bool IsReadonlyInventory { get; init; }
}

/// <summary>
/// Результат сканирования категории
/// </summary>
public record ScanResult
{
    public required string CategoryName { get; init; }
    public required string CategoryIcon { get; init; }
    public List<ScannedItem> Items { get; init; } = [];
    public long TotalSize => Items.Sum(i => i.Size);
    public int TotalCount => Items.Count;
}
