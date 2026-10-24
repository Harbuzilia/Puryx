using SmartCleaner.Core.Localization;

namespace SmartCleaner.Core.Startup;

/// <summary>
/// Источник записи автозагрузки.
/// </summary>
public enum StartupSource
{
    RegistryCurrentUser,   // HKCU\...\Run
    RegistryLocalMachine,  // HKLM\...\Run
    RegistryWow6432,       // HKLM\...\Run (WOW6432)
    StartupFolder,         // shell:startup (текущий пользователь)
    CommonStartupFolder,   // shell:common startup (все пользователи)
    TaskScheduler          // Планировщик заданий
}

/// <summary>
/// Элемент автозагрузки.
/// </summary>
public sealed class StartupItem
{
    /// <summary>Название (имя ключа или файла).</summary>
    public string Name { get; set; } = "";

    /// <summary>Полный путь к исполняемому файлу.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Аргументы командной строки (если есть).</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Полная команда (путь + аргументы).</summary>
    public string Command { get; set; } = "";

    /// <summary>Источник записи.</summary>
    public StartupSource Source { get; set; }

    /// <summary>Локализованное описание источника.</summary>
    public string SourceDescription => Source switch
    {
        StartupSource.RegistryCurrentUser => Localizer.GetString("Startup_Source_Hkcu"),
        StartupSource.RegistryLocalMachine => Localizer.GetString("Startup_Source_Hklm"),
        StartupSource.RegistryWow6432 => Localizer.GetString("Startup_Source_Wow64"),
        StartupSource.StartupFolder => Localizer.GetString("Startup_Source_UserFolder"),
        StartupSource.CommonStartupFolder => Localizer.GetString("Startup_Source_CommonFolder"),
        StartupSource.TaskScheduler => Localizer.GetString("Startup_Source_TaskScheduler"),
        _ => Localizer.GetString("Startup_Source_Unknown")
    };

    /// <summary>true = включён, false = отключён (для реестра: имя с "!" prefix).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Издатель (из свойств файла).</summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Полный путь задачи планировщика вида «\Папка\Задача» (для источника
    /// TaskScheduler): schtasks /Change /TN и /Delete /TN адресуют задачу
    /// именно путём, <see cref="Name"/> — лишь последний сегмент для отображения.
    /// </summary>
    public string TaskName { get; set; } = "";

    /// <summary>Путь к реестровому ключу (для Registry-записей).</summary>
    public string RegistryPath { get; set; } = "";

    /// <summary>Имя значения в реестре.</summary>
    public string RegistryValueName { get; set; } = "";
}
