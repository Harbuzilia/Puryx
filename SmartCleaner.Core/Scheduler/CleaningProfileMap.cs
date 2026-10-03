namespace SmartCleaner.Core.Scheduler;

/// <summary>
/// Маппинг имени профиля очистки в набор включаемых категорий сканеров.
/// Имена категорий синхронизированы с реальными CategoryName сканеров
/// (SmartCleaner.Core/Scanning/Scanners); UI (MainViewModel.ApplyProfile)
/// и CLI (--profile авто-очистки) используют этот маппинг как единый
/// источник — логика тестируемая без App.
/// </summary>
public static class CleaningProfileMap
{
    /// <summary>Быстрая очистка — базовые системные категории.</summary>
    public const string Quick = "Быстрая";

    /// <summary>Профиль разработчика — кэши и артефакты инструментов разработки.</summary>
    public const string Development = "Разработка";

    /// <summary>Полная очистка — все категории сканеров.</summary>
    public const string Full = "Полное";

    private static readonly IReadOnlySet<string> QuickCategories =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Система", "Браузеры", "Приложения"
        };

    private static readonly IReadOnlySet<string> DevelopmentCategories =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Кэши пакетов", "Node Modules", "NPM Packages", "Python Packages",
            "Python Venv", "Сборка .NET", "Docker", "AI-агенты"
        };

    /// <summary>
    /// Известные имена профилей — те же значения, что планировщик допускает
    /// в командной строке задачи (--profile).
    /// </summary>
    public static IReadOnlyList<string> KnownProfiles { get; } = [Quick, Development, Full];

    /// <summary>
    /// Разрешает набор включаемых категорий сканеров для профиля.
    /// «Полное» → true и categories = null: включены все категории.
    /// Неизвестный профиль → false (fallback выбирает вызывающая сторона,
    /// для авто-очистки это «Быстрая»).
    /// </summary>
    public static bool TryGetEnabledCategories(string? profileName, out IReadOnlySet<string>? categories)
    {
        switch (profileName)
        {
            case Quick:
                categories = QuickCategories;
                return true;
            case Development:
                categories = DevelopmentCategories;
                return true;
            case Full:
                categories = null;
                return true;
            default:
                categories = null;
                return false;
        }
    }
}
