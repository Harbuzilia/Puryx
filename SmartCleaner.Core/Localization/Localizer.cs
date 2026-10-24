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
    /// Форматированная строка (День 4, фаза L): значение ключа с плейсхолдерами
    /// {0}/{1}/… заполняется <see cref="string.Format(IFormatProvider, string, object[])"/>
    /// по ТЕКУЩЕЙ культуре — числа и даты локализуются («1.5» EN / «1,5» RU),
    /// а не инвариантно. Аргументов нет — эквивалент <see cref="GetString(string)"/>.
    /// Отсутствующий ключ возвращает имя ключа без форматирования (без {} в имени —
    /// исключения не будет).
    /// </summary>
    public static string GetString(string key, params object[] args)
    {
        var value = GetString(key);
        return args.Length == 0 ? value : string.Format(Culture ?? CultureInfo.InvariantCulture, value, args);
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
    public static string Duplicates_FindCopies => GetString(nameof(Duplicates_FindCopies));
    public static string Duplicates_CompareFolders => GetString(nameof(Duplicates_CompareFolders));

    // Автозагрузка
    public static string Startup_Title => GetString(nameof(Startup_Title));
    public static string Startup_Subtitle => GetString(nameof(Startup_Subtitle));
    public static string Startup_StatTotal => GetString(nameof(Startup_StatTotal));
    public static string Startup_StatActive => GetString(nameof(Startup_StatActive));
    public static string Startup_FilterLabel => GetString(nameof(Startup_FilterLabel));
    public static string Startup_FilterEnabled => GetString(nameof(Startup_FilterEnabled));
    public static string Startup_FilterDisabled => GetString(nameof(Startup_FilterDisabled));

    // Деинсталлятор
    public static string Uninstaller_Title => GetString(nameof(Uninstaller_Title));
    public static string Uninstaller_Subtitle => GetString(nameof(Uninstaller_Subtitle));
    public static string Uninstaller_RefreshList => GetString(nameof(Uninstaller_RefreshList));
    public static string Uninstaller_SearchPlaceholder => GetString(nameof(Uninstaller_SearchPlaceholder));
    public static string Uninstaller_StatTotalApps => GetString(nameof(Uninstaller_StatTotalApps));
    public static string Uninstaller_StatTotalSize => GetString(nameof(Uninstaller_StatTotalSize));
    public static string Uninstaller_ScanLeftovers => GetString(nameof(Uninstaller_ScanLeftovers));
    public static string Uninstaller_ScanLeftoversToolTip => GetString(nameof(Uninstaller_ScanLeftoversToolTip));
    public static string Uninstaller_Uninstall => GetString(nameof(Uninstaller_Uninstall));
    public static string Uninstaller_UninstallToolTip => GetString(nameof(Uninstaller_UninstallToolTip));
    public static string Uninstaller_LeftoversTitle => GetString(nameof(Uninstaller_LeftoversTitle));
    public static string Uninstaller_CleanSelectedLeftovers => GetString(nameof(Uninstaller_CleanSelectedLeftovers));

    // Карантин
    public static string Quarantine_Title => GetString(nameof(Quarantine_Title));
    public static string Quarantine_Subtitle => GetString(nameof(Quarantine_Subtitle));
    public static string Quarantine_ItemsMetric => GetString(nameof(Quarantine_ItemsMetric));
    public static string Quarantine_SizeMetric => GetString(nameof(Quarantine_SizeMetric));
    public static string Quarantine_OriginalPathPrefix => GetString(nameof(Quarantine_OriginalPathPrefix));
    public static string Quarantine_QuarantinedAtPrefix => GetString(nameof(Quarantine_QuarantinedAtPrefix));
    public static string Quarantine_Purge => GetString(nameof(Quarantine_Purge));

    // Планировщик
    public static string Scheduler_Title => GetString(nameof(Scheduler_Title));
    public static string Scheduler_Subtitle => GetString(nameof(Scheduler_Subtitle));
    public static string Scheduler_EnableAutoClean => GetString(nameof(Scheduler_EnableAutoClean));
    public static string Scheduler_SectionSchedule => GetString(nameof(Scheduler_SectionSchedule));
    public static string Scheduler_IntervalLabel => GetString(nameof(Scheduler_IntervalLabel));
    public static string Scheduler_DayOfWeekLabel => GetString(nameof(Scheduler_DayOfWeekLabel));
    public static string Scheduler_TimeLabel => GetString(nameof(Scheduler_TimeLabel));
    public static string Scheduler_SectionProfile => GetString(nameof(Scheduler_SectionProfile));
    public static string Scheduler_ProfileLabel => GetString(nameof(Scheduler_ProfileLabel));
    public static string Scheduler_RemoveTask => GetString(nameof(Scheduler_RemoveTask));
    public static string Scheduler_Save => GetString(nameof(Scheduler_Save));
    public static string Scheduler_SectionInfo => GetString(nameof(Scheduler_SectionInfo));
    public static string Scheduler_TaskStatusLabel => GetString(nameof(Scheduler_TaskStatusLabel));
    public static string Scheduler_LastRunLabel => GetString(nameof(Scheduler_LastRunLabel));

    // День 3, срез 2: Сжатие CompactOS
    public static string Compact_Title => GetString(nameof(Compact_Title));
    public static string Compact_Subtitle => GetString(nameof(Compact_Subtitle));
    public static string Compact_Discover => GetString(nameof(Compact_Discover));
    public static string Compact_StatTargets => GetString(nameof(Compact_StatTargets));
    public static string Compact_FoldersUnit => GetString(nameof(Compact_FoldersUnit));
    public static string Compact_StatCurrentSize => GetString(nameof(Compact_StatCurrentSize));
    public static string Compact_StatEstimatedSavings => GetString(nameof(Compact_StatEstimatedSavings));
    public static string Compact_NoFpsLoss => GetString(nameof(Compact_NoFpsLoss));
    public static string Compact_Decompress => GetString(nameof(Compact_Decompress));
    public static string Compact_Compress => GetString(nameof(Compact_Compress));

    // День 3, срез 2: Очистка компонентов ядра Windows
    public static string SystemDeepClean_Title => GetString(nameof(SystemDeepClean_Title));
    public static string SystemDeepClean_Subtitle => GetString(nameof(SystemDeepClean_Subtitle));
    public static string SystemDeepClean_RunAudit => GetString(nameof(SystemDeepClean_RunAudit));
    public static string SystemDeepClean_WinSxSTitle => GetString(nameof(SystemDeepClean_WinSxSTitle));
    public static string SystemDeepClean_CleanupRecommended => GetString(nameof(SystemDeepClean_CleanupRecommended));
    public static string SystemDeepClean_WinSxSDescription => GetString(nameof(SystemDeepClean_WinSxSDescription));
    public static string SystemDeepClean_ActualSizeLabel => GetString(nameof(SystemDeepClean_ActualSizeLabel));
    public static string SystemDeepClean_ReclaimableLabel => GetString(nameof(SystemDeepClean_ReclaimableLabel));
    public static string SystemDeepClean_CleanWinSxS => GetString(nameof(SystemDeepClean_CleanWinSxS));
    public static string SystemDeepClean_DriversTitle => GetString(nameof(SystemDeepClean_DriversTitle));
    public static string SystemDeepClean_DriversDescription => GetString(nameof(SystemDeepClean_DriversDescription));
    public static string SystemDeepClean_DeleteSelectedDrivers => GetString(nameof(SystemDeepClean_DeleteSelectedDrivers));
    public static string SystemDeepClean_StaleDuplicateBadge => GetString(nameof(SystemDeepClean_StaleDuplicateBadge));

    // День 3, срез 2: Плагины
    public static string Plugins_Title => GetString(nameof(Plugins_Title));
    public static string Plugins_Subtitle => GetString(nameof(Plugins_Subtitle));
    public static string Plugins_CleanSelected => GetString(nameof(Plugins_CleanSelected));
    public static string Plugins_Scan => GetString(nameof(Plugins_Scan));
    public static string Plugins_InstalledLabel => GetString(nameof(Plugins_InstalledLabel));

    // День 3, срез 2: AI-Ассистент
    public static string AiAssistant_Title => GetString(nameof(AiAssistant_Title));
    public static string AiAssistant_Subtitle => GetString(nameof(AiAssistant_Subtitle));
    public static string AiAssistant_InputPlaceholder => GetString(nameof(AiAssistant_InputPlaceholder));
    public static string AiAssistant_Send => GetString(nameof(AiAssistant_Send));

    // День 3, срез 2: Оптимизация памяти / Game Boost / SSD / Шредер
    public static string RamOptimizer_Title => GetString(nameof(RamOptimizer_Title));
    public static string RamOptimizer_Subtitle => GetString(nameof(RamOptimizer_Subtitle));
    public static string RamOptimizer_TabRam => GetString(nameof(RamOptimizer_TabRam));
    public static string RamOptimizer_StatTotal => GetString(nameof(RamOptimizer_StatTotal));
    public static string RamOptimizer_StatUsed => GetString(nameof(RamOptimizer_StatUsed));
    public static string RamOptimizer_LoadLabel => GetString(nameof(RamOptimizer_LoadLabel));
    public static string RamOptimizer_StatAvailable => GetString(nameof(RamOptimizer_StatAvailable));
    public static string RamOptimizer_StandbyTitle => GetString(nameof(RamOptimizer_StandbyTitle));
    public static string RamOptimizer_StandbyDescription => GetString(nameof(RamOptimizer_StandbyDescription));
    public static string RamOptimizer_Optimize => GetString(nameof(RamOptimizer_Optimize));
    public static string RamOptimizer_GameBoostDescription => GetString(nameof(RamOptimizer_GameBoostDescription));
    public static string RamOptimizer_ToggleGameBoost => GetString(nameof(RamOptimizer_ToggleGameBoost));
    public static string RamOptimizer_TabSsdHealth => GetString(nameof(RamOptimizer_TabSsdHealth));
    public static string RamOptimizer_RefreshDisks => GetString(nameof(RamOptimizer_RefreshDisks));
    public static string RamOptimizer_ColDisk => GetString(nameof(RamOptimizer_ColDisk));
    public static string RamOptimizer_ColMediaType => GetString(nameof(RamOptimizer_ColMediaType));
    public static string RamOptimizer_ColBus => GetString(nameof(RamOptimizer_ColBus));
    public static string RamOptimizer_ColSize => GetString(nameof(RamOptimizer_ColSize));
    public static string RamOptimizer_ColHealth => GetString(nameof(RamOptimizer_ColHealth));
    public static string RamOptimizer_ColRemainingLife => GetString(nameof(RamOptimizer_ColRemainingLife));
    public static string RamOptimizer_ColTemperature => GetString(nameof(RamOptimizer_ColTemperature));
    public static string RamOptimizer_TabSqlite => GetString(nameof(RamOptimizer_TabSqlite));
    public static string RamOptimizer_FindDatabases => GetString(nameof(RamOptimizer_FindDatabases));
    public static string RamOptimizer_CompactDatabases => GetString(nameof(RamOptimizer_CompactDatabases));
    public static string RamOptimizer_ColApp => GetString(nameof(RamOptimizer_ColApp));
    public static string RamOptimizer_ColDbName => GetString(nameof(RamOptimizer_ColDbName));
    public static string RamOptimizer_ColOriginalSize => GetString(nameof(RamOptimizer_ColOriginalSize));
    public static string RamOptimizer_ColCompactedSize => GetString(nameof(RamOptimizer_ColCompactedSize));
    public static string RamOptimizer_TabShredder => GetString(nameof(RamOptimizer_TabShredder));
    public static string RamOptimizer_ShredTitle => GetString(nameof(RamOptimizer_ShredTitle));
    public static string RamOptimizer_ShredDescription => GetString(nameof(RamOptimizer_ShredDescription));
    public static string RamOptimizer_ShredPathLabel => GetString(nameof(RamOptimizer_ShredPathLabel));
    public static string RamOptimizer_ShredFile => GetString(nameof(RamOptimizer_ShredFile));

    // День 3, срез 2: Оптимизация сети
    public static string NetworkOptimizer_Title => GetString(nameof(NetworkOptimizer_Title));
    public static string NetworkOptimizer_Subtitle => GetString(nameof(NetworkOptimizer_Subtitle));
    public static string NetworkOptimizer_ResetTitle => GetString(nameof(NetworkOptimizer_ResetTitle));
    public static string NetworkOptimizer_ResetDescription => GetString(nameof(NetworkOptimizer_ResetDescription));
    public static string NetworkOptimizer_FlushAction => GetString(nameof(NetworkOptimizer_FlushAction));
    public static string NetworkOptimizer_NoDelayTitle => GetString(nameof(NetworkOptimizer_NoDelayTitle));
    public static string NetworkOptimizer_NoDelayDescription => GetString(nameof(NetworkOptimizer_NoDelayDescription));
    public static string NetworkOptimizer_EnableNoDelay => GetString(nameof(NetworkOptimizer_EnableNoDelay));
    public static string NetworkOptimizer_DnsTableTitle => GetString(nameof(NetworkOptimizer_DnsTableTitle));
    public static string NetworkOptimizer_BenchmarkPing => GetString(nameof(NetworkOptimizer_BenchmarkPing));
    public static string NetworkOptimizer_ResetToDhcp => GetString(nameof(NetworkOptimizer_ResetToDhcp));
    public static string NetworkOptimizer_ColProvider => GetString(nameof(NetworkOptimizer_ColProvider));
    public static string NetworkOptimizer_ColPrimary => GetString(nameof(NetworkOptimizer_ColPrimary));
    public static string NetworkOptimizer_ColSecondary => GetString(nameof(NetworkOptimizer_ColSecondary));
    public static string NetworkOptimizer_ColLatency => GetString(nameof(NetworkOptimizer_ColLatency));
    public static string NetworkOptimizer_Apply => GetString(nameof(NetworkOptimizer_Apply));

    // День 3, срез 2: общие строки (новые повторы)
    public static string Common_Restore => GetString(nameof(Common_Restore));
    public static string Common_Disable => GetString(nameof(Common_Disable));
    public static string Common_Enable => GetString(nameof(Common_Enable));
    public static string Common_Scan => GetString(nameof(Common_Scan));

    // День 4, дедуп: одинаковые значения нескольких страниц → единые ключи
    public static string Common_Close => GetString(nameof(Common_Close));
    public static string Common_Duplicates => GetString(nameof(Common_Duplicates));
    public static string Common_ColStatus => GetString(nameof(Common_ColStatus));
    public static string Common_ColAction => GetString(nameof(Common_ColAction));
    public static string Common_CategoryLabel => GetString(nameof(Common_CategoryLabel));
    public static string Common_CategoryAll => GetString(nameof(Common_CategoryAll));

    // День 3, срез 2: Приватность / Anti-Spy
    public static string PrivacyDebloat_Title => GetString(nameof(PrivacyDebloat_Title));
    public static string PrivacyDebloat_Subtitle => GetString(nameof(PrivacyDebloat_Subtitle));
    public static string PrivacyDebloat_ApplyRecommended => GetString(nameof(PrivacyDebloat_ApplyRecommended));
    public static string PrivacyDebloat_ProtectedMetrics => GetString(nameof(PrivacyDebloat_ProtectedMetrics));
    public static string PrivacyDebloat_PrivacyActive => GetString(nameof(PrivacyDebloat_PrivacyActive));
    public static string PrivacyDebloat_DataCollectionBlocked => GetString(nameof(PrivacyDebloat_DataCollectionBlocked));
    public static string PrivacyDebloat_SafeRollback => GetString(nameof(PrivacyDebloat_SafeRollback));
    public static string PrivacyDebloat_JsonBackup => GetString(nameof(PrivacyDebloat_JsonBackup));
    public static string PrivacyDebloat_CategoryAll => GetString(nameof(PrivacyDebloat_CategoryAll));
    public static string PrivacyDebloat_CategoryTelemetry => GetString(nameof(PrivacyDebloat_CategoryTelemetry));
    public static string PrivacyDebloat_CategoryAds => GetString(nameof(PrivacyDebloat_CategoryAds));
    public static string PrivacyDebloat_CategoryReports => GetString(nameof(PrivacyDebloat_CategoryReports));
    public static string PrivacyDebloat_CategorySensors => GetString(nameof(PrivacyDebloat_CategorySensors));
    public static string PrivacyDebloat_StatusCollecting => GetString(nameof(PrivacyDebloat_StatusCollecting));
    public static string PrivacyDebloat_StatusBlocked => GetString(nameof(PrivacyDebloat_StatusBlocked));
    public static string PrivacyDebloat_FooterNote => GetString(nameof(PrivacyDebloat_FooterNote));

    // День 3, срез 2: Оптимизатор служб
    public static string ServicesOptimizer_Title => GetString(nameof(ServicesOptimizer_Title));
    public static string ServicesOptimizer_Subtitle => GetString(nameof(ServicesOptimizer_Subtitle));
    public static string ServicesOptimizer_RestoreServices => GetString(nameof(ServicesOptimizer_RestoreServices));
    public static string ServicesOptimizer_GamingProfile => GetString(nameof(ServicesOptimizer_GamingProfile));
    public static string ServicesOptimizer_GamingProfileHint => GetString(nameof(ServicesOptimizer_GamingProfileHint));
    public static string ServicesOptimizer_GamingProfileDescription => GetString(nameof(ServicesOptimizer_GamingProfileDescription));
    public static string ServicesOptimizer_ApplyProfile => GetString(nameof(ServicesOptimizer_ApplyProfile));
    public static string ServicesOptimizer_BalancedProfile => GetString(nameof(ServicesOptimizer_BalancedProfile));
    public static string ServicesOptimizer_BalancedProfileHint => GetString(nameof(ServicesOptimizer_BalancedProfileHint));
    public static string ServicesOptimizer_BalancedProfileDescription => GetString(nameof(ServicesOptimizer_BalancedProfileDescription));
    public static string ServicesOptimizer_WorkstationProfile => GetString(nameof(ServicesOptimizer_WorkstationProfile));
    public static string ServicesOptimizer_WorkstationProfileHint => GetString(nameof(ServicesOptimizer_WorkstationProfileHint));
    public static string ServicesOptimizer_WorkstationProfileDescription => GetString(nameof(ServicesOptimizer_WorkstationProfileDescription));
    public static string ServicesOptimizer_RiskFilterLabel => GetString(nameof(ServicesOptimizer_RiskFilterLabel));
    public static string ServicesOptimizer_RiskFilterAll => GetString(nameof(ServicesOptimizer_RiskFilterAll));
    public static string ServicesOptimizer_RiskFilterSafe => GetString(nameof(ServicesOptimizer_RiskFilterSafe));
    public static string ServicesOptimizer_RiskFilterModerate => GetString(nameof(ServicesOptimizer_RiskFilterModerate));
    public static string ServicesOptimizer_DisabledCountLabel => GetString(nameof(ServicesOptimizer_DisabledCountLabel));
    public static string ServicesOptimizer_CountSeparator => GetString(nameof(ServicesOptimizer_CountSeparator));
    public static string ServicesOptimizer_ColService => GetString(nameof(ServicesOptimizer_ColService));
    public static string ServicesOptimizer_ColCategory => GetString(nameof(ServicesOptimizer_ColCategory));
    public static string ServicesOptimizer_ColStatus => GetString(nameof(ServicesOptimizer_ColStatus));
    public static string ServicesOptimizer_ColStartupType => GetString(nameof(ServicesOptimizer_ColStartupType));
    public static string ServicesOptimizer_ColRisk => GetString(nameof(ServicesOptimizer_ColRisk));
    public static string ServicesOptimizer_FooterNote => GetString(nameof(ServicesOptimizer_FooterNote));

    // День 3, срез 2: Инспектор CLI
    public static string CliInspector_Title => GetString(nameof(CliInspector_Title));
    public static string CliInspector_Subtitle => GetString(nameof(CliInspector_Subtitle));
    public static string CliInspector_ExportReport => GetString(nameof(CliInspector_ExportReport));
    public static string CliInspector_StatTools => GetString(nameof(CliInspector_StatTools));
    public static string CliInspector_PiecesUnit => GetString(nameof(CliInspector_PiecesUnit));
    public static string CliInspector_StatDeadPaths => GetString(nameof(CliInspector_StatDeadPaths));
    public static string CliInspector_PathCountSeparator => GetString(nameof(CliInspector_PathCountSeparator));
    public static string CliInspector_StatPsProfiles => GetString(nameof(CliInspector_StatPsProfiles));
    public static string CliInspector_FoundUnit => GetString(nameof(CliInspector_FoundUnit));
    public static string CliInspector_TabTools => GetString(nameof(CliInspector_TabTools));
    public static string CliInspector_SearchPlaceholder => GetString(nameof(CliInspector_SearchPlaceholder));
    public static string CliInspector_ToolTipOpenInExplorer => GetString(nameof(CliInspector_ToolTipOpenInExplorer));
    public static string CliInspector_ToolTipOpenTerminal => GetString(nameof(CliInspector_ToolTipOpenTerminal));
    public static string CliInspector_ToolTipCopyUninstall => GetString(nameof(CliInspector_ToolTipCopyUninstall));
    public static string CliInspector_ToolTipDeleteToRecycleBin => GetString(nameof(CliInspector_ToolTipDeleteToRecycleBin));
    public static string CliInspector_TabPathHealth => GetString(nameof(CliInspector_TabPathHealth));
    public static string CliInspector_PathAnalyzerTitle => GetString(nameof(CliInspector_PathAnalyzerTitle));
    public static string CliInspector_PathAnalyzerDescription => GetString(nameof(CliInspector_PathAnalyzerDescription));
    public static string CliInspector_CleanDeadPaths => GetString(nameof(CliInspector_CleanDeadPaths));
    public static string CliInspector_BinCountLabel => GetString(nameof(CliInspector_BinCountLabel));
    public static string CliInspector_ToolTipRemovePathEntry => GetString(nameof(CliInspector_ToolTipRemovePathEntry));
    public static string CliInspector_PsProfilesTitle => GetString(nameof(CliInspector_PsProfilesTitle));
    public static string CliInspector_PsProfilesDescription => GetString(nameof(CliInspector_PsProfilesDescription));
    public static string CliInspector_ToolTipOpenFile => GetString(nameof(CliInspector_ToolTipOpenFile));
    public static string CliInspector_Open => GetString(nameof(CliInspector_Open));

    // День 3, срез 2: Диалог подтверждения очистки
    public static string PreviewDialog_Title => GetString(nameof(PreviewDialog_Title));
    public static string PreviewDialog_Header => GetString(nameof(PreviewDialog_Header));
    public static string PreviewDialog_Subheader => GetString(nameof(PreviewDialog_Subheader));
    public static string PreviewDialog_ItemsMetric => GetString(nameof(PreviewDialog_ItemsMetric));
    public static string PreviewDialog_DeleteMetric => GetString(nameof(PreviewDialog_DeleteMetric));
    public static string PreviewDialog_UninstallMetric => GetString(nameof(PreviewDialog_UninstallMetric));
    public static string PreviewDialog_SizeMetric => GetString(nameof(PreviewDialog_SizeMetric));
    public static string PreviewDialog_Confirm => GetString(nameof(PreviewDialog_Confirm));

    // День 3, срез 2: Автообнаружение приложений
    public static string Discovery_Title => GetString(nameof(Discovery_Title));
    public static string Discovery_Subtitle => GetString(nameof(Discovery_Subtitle));
    public static string Discovery_EmptyState => GetString(nameof(Discovery_EmptyState));
    public static string Discovery_CacheLabel => GetString(nameof(Discovery_CacheLabel));
    public static string Discovery_ProtectedLabel => GetString(nameof(Discovery_ProtectedLabel));
    public static string Discovery_AddAllWithCache => GetString(nameof(Discovery_AddAllWithCache));

    // День 3, срез 2: Настройки (разделы вокруг существующих Settings_Language*)
    public static string Settings_Title => GetString(nameof(Settings_Title));
    public static string Settings_Subtitle => GetString(nameof(Settings_Subtitle));
    public static string Settings_SectionAppearance => GetString(nameof(Settings_SectionAppearance));
    public static string Settings_ThemeLabel => GetString(nameof(Settings_ThemeLabel));
    public static string Settings_SectionMode => GetString(nameof(Settings_SectionMode));
    public static string Settings_ConfigModeLabel => GetString(nameof(Settings_ConfigModeLabel));
    public static string Settings_SectionCleaning => GetString(nameof(Settings_SectionCleaning));
    public static string Settings_UseRecycleBin => GetString(nameof(Settings_UseRecycleBin));
    public static string Settings_ProtectedPeriodLabel => GetString(nameof(Settings_ProtectedPeriodLabel));
    public static string Settings_HoursUnit => GetString(nameof(Settings_HoursUnit));
    public static string Settings_ProtectedPeriodHint => GetString(nameof(Settings_ProtectedPeriodHint));
    public static string Settings_SectionScanPaths => GetString(nameof(Settings_SectionScanPaths));
    public static string Settings_ScanPathsHint => GetString(nameof(Settings_ScanPathsHint));
    public static string Settings_BrowseFolderToolTip => GetString(nameof(Settings_BrowseFolderToolTip));
    public static string Settings_Add => GetString(nameof(Settings_Add));
    public static string Settings_SectionWhitelist => GetString(nameof(Settings_SectionWhitelist));
    public static string Settings_WhitelistHint => GetString(nameof(Settings_WhitelistHint));
    public static string Settings_ExamplesLabel => GetString(nameof(Settings_ExamplesLabel));
    public static string Settings_BuiltInPatternsNote => GetString(nameof(Settings_BuiltInPatternsNote));
    public static string Settings_SectionSound => GetString(nameof(Settings_SectionSound));
    public static string Settings_SoundCheckbox => GetString(nameof(Settings_SoundCheckbox));
    public static string Settings_SectionReport => GetString(nameof(Settings_SectionReport));
    public static string Settings_ReportDescription => GetString(nameof(Settings_ReportDescription));
    public static string Settings_GenerateReport => GetString(nameof(Settings_GenerateReport));
    public static string Settings_SectionShellIntegration => GetString(nameof(Settings_SectionShellIntegration));
    public static string Settings_ShellIntegrationHint => GetString(nameof(Settings_ShellIntegrationHint));
    public static string Settings_ShellAnalyzeMenu => GetString(nameof(Settings_ShellAnalyzeMenu));
    public static string Settings_ShellCompactMenu => GetString(nameof(Settings_ShellCompactMenu));
    public static string Settings_ShellShredMenu => GetString(nameof(Settings_ShellShredMenu));

    // День 4: строка со статусом результата сканирования (первый VM-ключ
    // с плейсхолдерами {0}/{1} — форматирование по текущей культуре)
    public static string Main_Status_Found => GetString(nameof(Main_Status_Found));

    // День 6 (часть б), фаза L: MainWindow, контентная зона (профили, статусы,
    // фильтры, таблица, оверлеи). Токены профилей (CommandParameter) остаются
    // константами CleaningProfileMap — ключи ниже только для отображения.
    public static string MainWindow_FreedAllTimePrefix => GetString(nameof(MainWindow_FreedAllTimePrefix));
    public static string MainWindow_SessionsSuffix => GetString(nameof(MainWindow_SessionsSuffix));
    public static string MainWindow_ScanConfigButton => GetString(nameof(MainWindow_ScanConfigButton));
    public static string MainWindow_ScanConfigToolTip => GetString(nameof(MainWindow_ScanConfigToolTip));
    public static string MainWindow_ProfileLabel => GetString(nameof(MainWindow_ProfileLabel));
    public static string Nav_Profile_Quick => GetString(nameof(Nav_Profile_Quick));
    public static string MainWindow_ProfileQuickToolTip => GetString(nameof(MainWindow_ProfileQuickToolTip));
    public static string Nav_Profile_Dev => GetString(nameof(Nav_Profile_Dev));
    public static string MainWindow_ProfileDevToolTip => GetString(nameof(MainWindow_ProfileDevToolTip));
    public static string Nav_Profile_Full => GetString(nameof(Nav_Profile_Full));
    public static string MainWindow_ProfileFullToolTip => GetString(nameof(MainWindow_ProfileFullToolTip));
    public static string MainWindow_ScanSectionLabel => GetString(nameof(MainWindow_ScanSectionLabel));
    public static string MainWindow_CustomFolderLabel => GetString(nameof(MainWindow_CustomFolderLabel));
    public static string MainWindow_Browse => GetString(nameof(MainWindow_Browse));
    public static string MainWindow_StatFoundJunk => GetString(nameof(MainWindow_StatFoundJunk));
    public static string MainWindow_StatSelected => GetString(nameof(MainWindow_StatSelected));
    public static string MainWindow_StatItems => GetString(nameof(MainWindow_StatItems));
    public static string MainWindow_ScanButtonSubtitle => GetString(nameof(MainWindow_ScanButtonSubtitle));
    public static string MainWindow_FilterSource => GetString(nameof(MainWindow_FilterSource));
    public static string MainWindow_FilterScope => GetString(nameof(MainWindow_FilterScope));
    public static string MainWindow_ColPath => GetString(nameof(MainWindow_ColPath));
    public static string MainWindow_ColSize => GetString(nameof(MainWindow_ColSize));
    public static string MainWindow_ColStatus => GetString(nameof(MainWindow_ColStatus));
    public static string MainWindow_MenuOpenFolder => GetString(nameof(MainWindow_MenuOpenFolder));
    public static string MainWindow_MenuCopyPath => GetString(nameof(MainWindow_MenuCopyPath));
    public static string MainWindow_MenuAddToWhitelist => GetString(nameof(MainWindow_MenuAddToWhitelist));
    public static string MainWindow_EmptyState => GetString(nameof(MainWindow_EmptyState));
    public static string MainWindow_SelectSafe => GetString(nameof(MainWindow_SelectSafe));
    public static string MainWindow_SelectCaches => GetString(nameof(MainWindow_SelectCaches));
    public static string MainWindow_SelectCachesToolTip => GetString(nameof(MainWindow_SelectCachesToolTip));
    public static string MainWindow_DeselectAll => GetString(nameof(MainWindow_DeselectAll));
    public static string MainWindow_Export => GetString(nameof(MainWindow_Export));
    public static string MainWindow_CleanSelected => GetString(nameof(MainWindow_CleanSelected));
    public static string MainWindow_ScanOverlayTitle => GetString(nameof(MainWindow_ScanOverlayTitle));
    public static string MainWindow_CancelScan => GetString(nameof(MainWindow_CancelScan));
    public static string MainWindow_CleanOverlayTitle => GetString(nameof(MainWindow_CleanOverlayTitle));
}
