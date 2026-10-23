using System.Collections;
using System.Globalization;
using System.Resources;

namespace SmartCleaner.Core.Localization;

/// <summary>
/// Локализатор Puryx (ROADMAP v3.1, фаза L): единая точка доступа к строкам UI.
/// Нейтральная культура — EN (<c>Strings.resx</c>), русская — <c>Strings.ru.resx</c>;
/// паритет ключей между культурами — инвариант-тест (<c>LocalizationTests</c>).
/// XAML использует <c>{x:Static loc:Localizer.Key}</c>: статические свойства ниже —
/// тонкие обёртки GetString, их имена обязаны совпадать с ключами resx
/// (проверяется тестом Localizer_StaticProperties_MatchResxKeys).
/// Смена языка применяется при перезапуске приложения.
/// </summary>
public static class Localizer
{
    /// <summary>Базовое имя ресурса: manifest = RootNamespace.Localization.Strings.</summary>
    private const string ResourceBaseName = "SmartCleaner.Core.Localization.Strings";

    private static readonly ResourceManager ResourceManager =
        new(ResourceBaseName, typeof(Localizer).Assembly);

    /// <summary>
    /// Культура выборки строк. null (до Init) — детерминированно нейтральная EN,
    /// без зависимости от культуры машины.
    /// </summary>
    public static CultureInfo? Culture { get; set; }

    /// <summary>
    /// Инициализация при старте приложения: применяет сохранённую настройку языка
    /// ("Auto" | "ru" | "en"). Auto/пусто/неизвестное значение — авто-детект по
    /// <see cref="CultureInfo.CurrentUICulture"/> (ru* → русский, остальное →
    /// нейтральная EN).
    /// </summary>
    public static void Init(string? languageSetting)
    {
        Culture = ResolveCulture(languageSetting, CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>
    /// Чистая функция разрешения культуры (шов CurrentUICulture через параметр —
    /// тестируемо): явная настройка приоритетнее авто-детекта; "Auto"/null/пусто —
    /// детект по имени культуры UI (ru* → ru, остальное → нейтральная).
    /// </summary>
    public static CultureInfo ResolveCulture(string? languageSetting, string? uiCultureName)
    {
        if (!string.IsNullOrWhiteSpace(languageSetting) &&
            !string.Equals(languageSetting.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(languageSetting.Trim(), "ru", StringComparison.OrdinalIgnoreCase)
                ? new CultureInfo("ru")
                : CultureInfo.InvariantCulture;
        }

        var isRussian = !string.IsNullOrEmpty(uiCultureName) &&
                        uiCultureName.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        return isRussian ? new CultureInfo("ru") : CultureInfo.InvariantCulture;
    }

    /// <summary>
    /// Строка по ключу для текущей <see cref="Culture"/>. Отсутствующий или пустой
    /// ключ не бросает исключение — возвращается имя ключа: читаемый fallback
    /// (дефект виден глазами), XAML-загрузка не падает.
    /// </summary>
    public static string GetString(string key)
    {
        var value = ResourceManager.GetString(key, Culture ?? CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    /// <summary>
    /// Ключи культуры без родительского фолбэка — для инварианта паритета EN↔RU
    /// и диагностики (какие ключи есть в resx конкретной культуры).
    /// </summary>
    public static IReadOnlyList<string> GetKeys(CultureInfo culture)
    {
        var resourceSet = ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        if (resourceSet is null)
        {
            return [];
        }

        var keys = new List<string>();
        foreach (DictionaryEntry entry in resourceSet)
        {
            keys.Add((string)entry.Key);
        }

        return keys;
    }

    // Пилотные ключи Дня 1 (MainWindow / About / настройка языка). Имена свойств
    // обязаны совпадать с ключами resx — инвариант Localizer_StaticProperties_MatchResxKeys.

    // MainWindow
    public static string MainWindow_Title => GetString(nameof(MainWindow_Title));
    public static string MainWindow_Tagline => GetString(nameof(MainWindow_Tagline));

    // Сайдбар: секции
    public static string Nav_Section_Cleanup => GetString(nameof(Nav_Section_Cleanup));
    public static string Nav_Section_Tools => GetString(nameof(Nav_Section_Tools));
    public static string Nav_Section_System => GetString(nameof(Nav_Section_System));

    // Сайдбар: раздел «Очистка»
    public static string Nav_Overview => GetString(nameof(Nav_Overview));

    // Сайдбар: раздел «Инструменты»
    public static string Nav_Duplicates => GetString(nameof(Nav_Duplicates));
    public static string Nav_DiskMap => GetString(nameof(Nav_DiskMap));
    public static string Nav_LargeFiles => GetString(nameof(Nav_LargeFiles));
    public static string Nav_Startup => GetString(nameof(Nav_Startup));
    public static string Nav_Discovery => GetString(nameof(Nav_Discovery));
    public static string Nav_CliInspector => GetString(nameof(Nav_CliInspector));
    public static string Nav_AiAssistant => GetString(nameof(Nav_AiAssistant));
    public static string Nav_Uninstaller => GetString(nameof(Nav_Uninstaller));
    public static string Nav_Compact => GetString(nameof(Nav_Compact));
    public static string Nav_WinSxS => GetString(nameof(Nav_WinSxS));
    public static string Nav_Quarantine => GetString(nameof(Nav_Quarantine));
    public static string Nav_Plugins => GetString(nameof(Nav_Plugins));
    public static string Nav_RamOptimizer => GetString(nameof(Nav_RamOptimizer));
    public static string Nav_NetworkOptimizer => GetString(nameof(Nav_NetworkOptimizer));

    // Сайдбар: раздел «Система»
    public static string Nav_Privacy => GetString(nameof(Nav_Privacy));
    public static string Nav_ServicesOptimizer => GetString(nameof(Nav_ServicesOptimizer));
    public static string Nav_Dashboard => GetString(nameof(Nav_Dashboard));
    public static string Nav_Scheduler => GetString(nameof(Nav_Scheduler));
    public static string Nav_Settings => GetString(nameof(Nav_Settings));

    // Футер сайдбара
    public static string Footer_ThemeButton => GetString(nameof(Footer_ThemeButton));
    public static string Footer_ThemeButtonToolTip => GetString(nameof(Footer_ThemeButtonToolTip));
    public static string Footer_AboutButtonToolTip => GetString(nameof(Footer_AboutButtonToolTip));

    // Окно «О программе»
    public static string About_Title => GetString(nameof(About_Title));
    public static string About_Subtitle => GetString(nameof(About_Subtitle));
    public static string About_TechSpec => GetString(nameof(About_TechSpec));
    public static string About_Copyright => GetString(nameof(About_Copyright));
    public static string About_CloseButton => GetString(nameof(About_CloseButton));

    // Настройки: язык
    public static string Settings_Language => GetString(nameof(Settings_Language));
    public static string Settings_LanguageAuto => GetString(nameof(Settings_LanguageAuto));
    public static string Settings_LanguageDescription => GetString(nameof(Settings_LanguageDescription));
    public static string Settings_LanguageRestartHint => GetString(nameof(Settings_LanguageRestartHint));
}
