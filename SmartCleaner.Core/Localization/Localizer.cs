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

    // День 2, срез 1: общие строки страниц (идентичные литералы нескольких страниц)
    public static string Common_Refresh => GetString(nameof(Common_Refresh));
    public static string Common_Cancel => GetString(nameof(Common_Cancel));
    public static string Common_Path => GetString(nameof(Common_Path));
    public static string Common_OpenInExplorer => GetString(nameof(Common_OpenInExplorer));

    // Дашборд
    public static string Dashboard_Title => GetString(nameof(Dashboard_Title));
    public static string Dashboard_Subtitle => GetString(nameof(Dashboard_Subtitle));
    public static string Dashboard_ClearHistory => GetString(nameof(Dashboard_ClearHistory));
    public static string Dashboard_KpiSessions => GetString(nameof(Dashboard_KpiSessions));
    public static string Dashboard_KpiTotalFreed => GetString(nameof(Dashboard_KpiTotalFreed));
    public static string Dashboard_KpiFilesRemoved => GetString(nameof(Dashboard_KpiFilesRemoved));
    public static string Dashboard_KpiAveragePerSession => GetString(nameof(Dashboard_KpiAveragePerSession));
    public static string Dashboard_ChartPeriodLabel => GetString(nameof(Dashboard_ChartPeriodLabel));
    public static string Dashboard_ChartPeriodMonthly => GetString(nameof(Dashboard_ChartPeriodMonthly));
    public static string Dashboard_ChartPeriodWeekly => GetString(nameof(Dashboard_ChartPeriodWeekly));
    public static string Dashboard_ChartMonthlyTitle => GetString(nameof(Dashboard_ChartMonthlyTitle));
    public static string Dashboard_ChartWeeklyTitle => GetString(nameof(Dashboard_ChartWeeklyTitle));

    // Карта диска
    public static string DiskMap_Title => GetString(nameof(DiskMap_Title));
    public static string DiskMap_Subtitle => GetString(nameof(DiskMap_Subtitle));
    public static string DiskMap_Analyze => GetString(nameof(DiskMap_Analyze));
    public static string DiskMap_Depth => GetString(nameof(DiskMap_Depth));
    public static string DiskMap_InfoTitle => GetString(nameof(DiskMap_InfoTitle));
    public static string DiskMap_GoToRoot => GetString(nameof(DiskMap_GoToRoot));

    // Большие файлы
    public static string LargeFiles_Title => GetString(nameof(LargeFiles_Title));
    public static string LargeFiles_Subtitle => GetString(nameof(LargeFiles_Subtitle));
    public static string LargeFiles_Find => GetString(nameof(LargeFiles_Find));
    public static string LargeFiles_MinSize => GetString(nameof(LargeFiles_MinSize));
    public static string LargeFiles_ToolTipDeleteToRecycleBin => GetString(nameof(LargeFiles_ToolTipDeleteToRecycleBin));
    public static string LargeFiles_Total => GetString(nameof(LargeFiles_Total));

    // Дубликаты
    public static string Duplicates_Title => GetString(nameof(Duplicates_Title));
    public static string Duplicates_Subtitle => GetString(nameof(Duplicates_Subtitle));
    public static string Duplicates_TabDuplicates => GetString(nameof(Duplicates_TabDuplicates));
    public static string Duplicates_TabBySample => GetString(nameof(Duplicates_TabBySample));
    public static string Duplicates_TabCompare => GetString(nameof(Duplicates_TabCompare));
    public static string Duplicates_TabDeletionLog => GetString(nameof(Duplicates_TabDeletionLog));
    public static string Duplicates_ScanFoldersLabel => GetString(nameof(Duplicates_ScanFoldersLabel));
    public static string Duplicates_ExcludeLabel => GetString(nameof(Duplicates_ExcludeLabel));
    public static string Duplicates_CriteriaLabel => GetString(nameof(Duplicates_CriteriaLabel));
    public static string Duplicates_OptBySize => GetString(nameof(Duplicates_OptBySize));
    public static string Duplicates_OptByHash => GetString(nameof(Duplicates_OptByHash));
    public static string Duplicates_OptByName => GetString(nameof(Duplicates_OptByName));
    public static string Duplicates_OptByByte => GetString(nameof(Duplicates_OptByByte));
    public static string Duplicates_OptTurbo => GetString(nameof(Duplicates_OptTurbo));
    public static string Duplicates_MinFileSizeLabel => GetString(nameof(Duplicates_MinFileSizeLabel));
    public static string Duplicates_StatGroups => GetString(nameof(Duplicates_StatGroups));
    public static string Duplicates_StatDuplicates => GetString(nameof(Duplicates_StatDuplicates));
    public static string Duplicates_StatWasted => GetString(nameof(Duplicates_StatWasted));
    public static string Duplicates_GroupFilesTimes => GetString(nameof(Duplicates_GroupFilesTimes));
    public static string Duplicates_PreviewTitle => GetString(nameof(Duplicates_PreviewTitle));
    public static string Duplicates_SampleFileLabel => GetString(nameof(Duplicates_SampleFileLabel));
    public static string Duplicates_SampleSearchFoldersLabel => GetString(nameof(Duplicates_SampleSearchFoldersLabel));
    public static string Duplicates_FolderALabel => GetString(nameof(Duplicates_FolderALabel));
    public static string Duplicates_FolderBLabel => GetString(nameof(Duplicates_FolderBLabel));
    public static string Duplicates_StatOnlyInA => GetString(nameof(Duplicates_StatOnlyInA));
    public static string Duplicates_StatOnlyInB => GetString(nameof(Duplicates_StatOnlyInB));
    public static string Duplicates_StatCommon => GetString(nameof(Duplicates_StatCommon));
    public static string Duplicates_ColStatus => GetString(nameof(Duplicates_ColStatus));
    public static string Duplicates_ColPath => GetString(nameof(Duplicates_ColPath));
    public static string Duplicates_ColSizeA => GetString(nameof(Duplicates_ColSizeA));
    public static string Duplicates_ColSizeB => GetString(nameof(Duplicates_ColSizeB));
    public static string Duplicates_ColNewer => GetString(nameof(Duplicates_ColNewer));
    public static string Duplicates_OpenRecycleBin => GetString(nameof(Duplicates_OpenRecycleBin));
    public static string Duplicates_ClearLog => GetString(nameof(Duplicates_ClearLog));
    public static string Duplicates_LogFilesBullet => GetString(nameof(Duplicates_LogFilesBullet));
    public static string Duplicates_ReplaceWithHardlinks => GetString(nameof(Duplicates_ReplaceWithHardlinks));
    public static string Duplicates_ReplaceWithHardlinksToolTip => GetString(nameof(Duplicates_ReplaceWithHardlinksToolTip));
    public static string Duplicates_DeleteSelected => GetString(nameof(Duplicates_DeleteSelected));
    public static string Duplicates_Scan => GetString(nameof(Duplicates_Scan));
    public static string Duplicates_FindCopies => GetString(nameof(Duplicates_FindCopies));
    public static string Duplicates_CompareFolders => GetString(nameof(Duplicates_CompareFolders));
}
