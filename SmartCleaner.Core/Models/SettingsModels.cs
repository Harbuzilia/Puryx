namespace SmartCleaner.Core.Models;

public sealed class ScanPathsConfig
{
    public List<string> Paths { get; set; } = [];
}

public sealed class CleaningSettingsConfig
{
    public bool UseRecycleBin { get; set; } = true;
    public string Theme { get; set; } = "System";
}

public sealed class UiSettingsConfig
{
    public string Theme { get; set; } = "System";

    /// <summary>Язык интерфейса: "Auto" | "ru" | "en" (ROADMAP v3.1, фаза L).</summary>
    public string Language { get; set; } = "Auto";
}

/// <summary>
/// Настройки сканирования (ROADMAP v3.1, День 9, фаза M): единая точка выбора
/// режима MFT для DiskUsage/LargeFiles/DiskMap — не хардкодится в сканеры.
/// </summary>
public sealed class ScanSettingsConfig
{
    /// <summary>Имя файла единой настройки сканирования (config-каталог).</summary>
    public const string Filename = "scan_settings.json";

    /// <summary>
    /// Режим MFT-сканирования: "Auto" (NTFS + права → MFT, иначе фолбэк) |
    /// "On" (всегда попытка) | "Off" (никогда MFT, обход каталогов).
    /// </summary>
    public string MftScanMode { get; set; } = "Auto";

    /// <summary>
    /// Искать cookie-хранилища браузеров (ROADMAP v3.1, День 14, фаза B).
    /// По умолчанию <c>false</c>: без явного согласия сканер НЕ выдаёт cookie-элементы
    /// вообще (не «не выбирает», а не показывает). При <c>true</c> элементы выдаются
    /// с <see cref="RiskCategory.UserData"/> и <see cref="ScannedItem.IsUserDataOptIn"/>,
    /// всегда невыбранными — удаление только вручную и с подтверждением.
    /// </summary>
    public bool BrowserCleanCookies { get; set; }

    /// <summary>
    /// Глубокий проход «системного мусора» (ROADMAP v3.1, День 15, фаза F):
    /// рекурсивно искать <c>.chk</c>/<c>.tmp</c>/<c>.dmp</c> внутри ОГРАНИЧЕННЫХ
    /// корней (%TEMP% пользователя, Windows\Temp, WER/Minidump, <c>FOUND.*</c>).
    /// По умолчанию <c>false</c>: расширенный проход по подкаталогам — только по
    /// явному согласию; при выключенной опции SystemScanner работает как раньше.
    /// </summary>
    public bool DeepJunkScan { get; set; }
}

/// <summary>
/// Настройки трея и фоновой проверки обновлений (ROADMAP v3.1, День 13, фаза A).
/// Обе опции выключены по умолчанию: резидентность и сетевые обращения —
/// только по явному согласию (приватность — позиция проекта).
/// Контракт поведения (см. docs/TRAY-MODE.md):
/// <see cref="MinimizeToTray"/> управляет ТОЛЬКО кнопкой «свернуть»;
/// закрытие окна (X) всегда завершает приложение — одна опция, один смысл.
/// Имя файла — <see cref="Filename"/> (config-каталог, единый механизм
/// с ui_settings.json/scan_settings.json).
/// </summary>
public sealed class TraySettingsConfig
{
    /// <summary>Имя файла настроек трея в config-каталоге.</summary>
    public const string Filename = "tray_settings.json";

    /// <summary>Сворачивать окно в трей по Minimize вместо панели задач.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>
    /// Фоновая проверка обновлений раз в сутки, пока приложение запущено.
    /// Только уведомление: скачивание/установка — по явному действию пользователя.
    /// </summary>
    public bool AutoCheckUpdates { get; set; }
}

/// <summary>
/// Состояние фоновой проверки обновлений (ROADMAP v3.1, День 13, фаза A):
/// UTC-отметка последней авто-проверки переживает перезапуски, чтобы проверка
/// шла НЕ чаще раза в сутки (иначе каждый старт = сетевой запрос).
/// Имя файла — <see cref="Filename"/> (config-каталог).
/// </summary>
public sealed class UpdateStateConfig
{
    /// <summary>Имя файла состояния авто-проверки в config-каталоге.</summary>
    public const string Filename = "update_state.json";

    /// <summary>UTC-время последней авто-проверки; null — проверок ещё не было.</summary>
    public DateTimeOffset? LastAutoCheckUtc { get; set; }
}
