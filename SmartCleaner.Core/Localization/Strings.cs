namespace SmartCleaner.Core.Localization;

/// <summary>
/// Фундамент локализации — статический класс строковых ресурсов.
/// В будущем можно заменить на .resx или IStringLocalizer.
/// </summary>
public static class Strings
{
    // — UI: Главное окно —
    public const string AppTitle = "Умная очистка диска";
    public const string ReadyToScan = "Готов к сканированию";
    public const string ScanButton = "Сканировать";
    public const string CleanButton = "Очистить выбранные";
    public const string CancelScan = "Отменить сканирование";
    public const string ExportButton = "📥 Экспорт";
    public const string AboutButton = "ℹ️ О программе";

    // — UI: Карточки —
    public const string FoundLabel = "Найдено";
    public const string SelectedLabel = "Выбрано";
    public const string ElementsLabel = "Элементов";
    public const string TotalFreedLabel = "Всего за";
    public const string SessionsLabel = "сессий";

    // — UI: Footer —
    public const string SelectSafe = "Выбрать безопасные";
    public const string SelectCache = "+ кэши";
    public const string Deselect = "Снять выбор";
    public const string SelectCacheTooltip = "Выбрать SafeToDelete + PerformanceCache";

    // — UI: Контекстное меню —
    public const string OpenFolder = "📂 Открыть папку";
    public const string CopyPath = "📋 Копировать путь";
    public const string AddToWhitelist = "🛡️ Добавить в белый список";

    // — UI: Sidebar —
    public const string Overview = "Обзор";
    public const string Categories = "КАТЕГОРИИ";
    public const string Settings = "Настройки";
    public const string Discovery = "Обнаружение";

    // — UI: Сортировка —
    public const string SortByPath = "Путь ↕";
    public const string SortBySize = "Размер ↕";
    public const string SortByStatus = "Статус ↕";

    // — UI: Фильтры —
    public const string FilterAll = "Все";
    public const string FilterNpm = "NPM";
    public const string FilterPython = "Python";

    // — Статусы —
    public const string ScanCancelled = "Сканирование отменено";
    public const string CleaningPackages = "Обслуживание пакетов...";
    public static string CleaningCompleted(string size, int count)
        => $"✅ Очистка завершена: {size} освобождено ({count} файлов)";
    public static string ScanningFolders(int count)
        => $"Сканирование {count} папок...";
    public static string FoundInFolders(int items, int folders)
        => $"Найдено {items} элементов в {folders} папках";
    public static string DragFoldersHint => "⚠️ Перетащите папки для сканирования";

    // — About —
    public const string AboutTitle = "О программе — Smart Cleaner";
    public const string AboutVersion = "Версия 1.0.0";
    public const string AboutDescription = "Умная очистка диска Windows";
    public const string AboutTech = ".NET 9 · WPF · MVVM · 11 сканеров";
    public const string CloseButton = "Закрыть";
}
