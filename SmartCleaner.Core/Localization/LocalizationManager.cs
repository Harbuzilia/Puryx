namespace SmartCleaner.Core.Localization;

public enum AppLanguage
{
    Russian,
    English
}

public class LocalizationManager
{
    public static LocalizationManager Instance { get; } = new();

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.Russian;
    public event Action? LanguageChanged;

    private readonly Dictionary<string, string> _ruStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "Smart System Cleaner (CHISTilka)",
        ["Scan"] = "Анализ системы",
        ["Clean"] = "Очистить",
        ["Cancel"] = "Отмена",
        ["Settings"] = "Настройки",
        ["Duplicates"] = "Дубликаты",
        ["DiskMap"] = "Карта диска",
        ["LargeFiles"] = "Большие файлы",
        ["Startup"] = "Автозагрузка",
        ["Uninstaller"] = "Деинсталлятор",
        ["CompactOS"] = "Сжатие CompactOS",
        ["WinSxS"] = "Очистка ядра WinSxS",
        ["Quarantine"] = "Карантин Sandbox",
        ["Plugins"] = "Плагины сообщества",
        ["RamOptimizer"] = "Оптимизация RAM & БД",
        ["AiAssistant"] = "AI Ассистент"
    };

    private readonly Dictionary<string, string> _enStrings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AppTitle"] = "Smart System Cleaner (CHISTilka)",
        ["Scan"] = "Scan System",
        ["Clean"] = "Clean Selected",
        ["Cancel"] = "Cancel",
        ["Settings"] = "Settings",
        ["Duplicates"] = "Duplicates",
        ["DiskMap"] = "Disk Treemap",
        ["LargeFiles"] = "Large Files",
        ["Startup"] = "Startup Apps",
        ["Uninstaller"] = "Deep Uninstaller",
        ["CompactOS"] = "CompactOS Compression",
        ["WinSxS"] = "WinSxS Kernel Clean",
        ["Quarantine"] = "Sandbox Quarantine",
        ["Plugins"] = "Community Plugins",
        ["RamOptimizer"] = "RAM & DB Optimizer",
        ["AiAssistant"] = "AI Assistant"
    };

    public string GetString(string key)
    {
        var dict = CurrentLanguage == AppLanguage.Russian ? _ruStrings : _enStrings;
        if (dict.TryGetValue(key, out var val)) return val;
        return key;
    }

    public void SetLanguage(AppLanguage language)
    {
        if (CurrentLanguage == language) return;
        CurrentLanguage = language;
        LanguageChanged?.Invoke();
    }
}
