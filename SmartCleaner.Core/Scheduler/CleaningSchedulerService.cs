using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Text.Json;
// UseWindowsForms тянет System.Windows.Forms.ICommandExecutor — снимаем
// неоднозначность в пользу контракта исполнителя команд
using ICommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

namespace SmartCleaner.Core.Scheduler;

/// <summary>
/// Настройки планировщика очистки.
/// </summary>
public sealed class ScheduleSettings
{
    /// <summary>Включён ли планировщик.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Интервал: Daily, Weekly, Monthly.</summary>
    public string Interval { get; set; } = "Weekly";

    /// <summary>День недели (для Weekly): 1=Mon..7=Sun.</summary>
    public int DayOfWeek { get; set; } = 1;

    /// <summary>Час запуска.</summary>
    public int Hour { get; set; } = 3;

    /// <summary>Минуты запуска.</summary>
    public int Minute { get; set; } = 0;

    /// <summary>Профиль очистки: Быстрая, Разработка, Полное.</summary>
    public string Profile { get; set; } = "Быстрая";

    /// <summary>Дата последнего запуска.</summary>
    public DateTime? LastRun { get; set; }
}

/// <summary>
/// Сервис планирования автоматической очистки через Windows Task Scheduler.
/// Создаёт/обновляет/удаляет задачу в планировщике Windows.
/// День 19, срез C: операции с планировщиком (schtasks) — через контракт
/// ICommandExecutor; методы асинхронны (исполнитель асинхронен, sync-over-async
/// запрещён — паттерн Дня 17).
/// </summary>
public sealed class CleaningSchedulerService
{
    private const string TaskName = "SmartCleaner_AutoClean";
    private readonly string _settingsPath;
    private readonly ICommandExecutor _commandExecutor;

    public CleaningSchedulerService(ICommandExecutor? commandExecutor = null)
    {
        _commandExecutor = commandExecutor ?? new ProcessCommandExecutor();
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SmartCleaner");
        try
        {
            Directory.CreateDirectory(appData);
        }
        catch
        {
            // Если не удалось создать директорию — работаем без сохранения настроек
        }
        _settingsPath = Path.Combine(appData, "schedule_settings.json");
    }

    /// <summary>
    /// Загружает настройки планировщика из файла.
    /// </summary>
    public ScheduleSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<ScheduleSettings>(json) ?? new ScheduleSettings();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Load error: {ex.Message}"); }

        return new ScheduleSettings();
    }

    /// <summary>
    /// Сохраняет настройки планировщика.
    /// </summary>
    public void SaveSettings(ScheduleSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Save error: {ex.Message}"); }
    }

    /// <summary>
    /// Профиль очистки для аргумента --profile. Значение попадает в командную строку
    /// schtasks, поэтому допускаются только заранее известные варианты
    /// (единый источник — CleaningProfileMap.KnownProfiles).
    /// </summary>
    private static readonly string[] AllowedProfiles = [.. CleaningProfileMap.KnownProfiles];

    /// <summary>
    /// Регистрирует или обновляет задачу в Windows Task Scheduler.
    /// Возвращает true при успехе.
    /// </summary>
    public async Task<bool> RegisterTaskAsync(ScheduleSettings settings)
    {
        try
        {
            // Сначала удалим существующую задачу
            await UnregisterTaskAsync();

            if (!settings.IsEnabled) return true;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var profile = AllowedProfiles.Contains(settings.Profile) ? settings.Profile : AllowedProfiles[0];

            // Формируем расписание для schtasks
            var scheduleType = settings.Interval switch
            {
                "Daily" => "DAILY",
                "Monthly" => "MONTHLY",
                _ => "WEEKLY"
            };

            var startTime = $"{settings.Hour:D2}:{settings.Minute:D2}";

            // /TR несёт встроенные кавычки (команда задачи) — один элемент
            // ArgumentList: исполнитель квотует его с экранированием, фактическая
            // командная строка прежняя («"exe" --auto-clean --profile "…"»)
            var arguments = new List<string>
            {
                "/Create", "/TN", TaskName,
                "/TR", $"\"{exePath}\" --auto-clean --profile \"{profile}\"",
                "/SC", scheduleType, "/ST", startTime,
                "/F", "/RL", "LIMITED"
            };

            if (scheduleType == "WEEKLY")
            {
                arguments.Add("/D");
                arguments.Add(GetDayName(settings.DayOfWeek));
            }

            var result = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Абсолютный путь из системного каталога: запуск по неквалифицированному
                // имени ищет exe в каталоге приложения — binary planting (M7, День 16б)
                FileName = SystemToolLocator.GetSchtasksPath(),
                Arguments = arguments,
                WorkingDirectory = string.Empty,
                Timeout = RegisterTimeout
            });
            return result.ExitCode == 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Register error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Удаляет задачу из Windows Task Scheduler.
    /// </summary>
    public async Task<bool> UnregisterTaskAsync()
    {
        try
        {
            await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetSchtasksPath(),
                Arguments = ["/Delete", "/TN", TaskName, "/F"],
                WorkingDirectory = string.Empty,
                Timeout = UnregisterTimeout
            });
            return true; // OK даже если задачи не было (exit не проверяем — прежняя семантика)
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Unregister error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Проверяет, зарегистрирована ли задача.
    /// </summary>
    public async Task<bool> IsTaskRegisteredAsync()
    {
        try
        {
            var result = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Абсолютный путь из системного каталога — binary planting (M7)
                FileName = SystemToolLocator.GetSchtasksPath(),
                Arguments = ["/Query", "/TN", TaskName],
                WorkingDirectory = string.Empty,
                Timeout = QueryTimeout
            });
            return result.ExitCode == 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Query error: {ex.Message}"); return false; }
    }

    // Таймауты schtasks-операций (День 19) — как в прежнем коде
    // (WaitForExit 10 с / 5 с / 5 с), но теперь bounded честно: kill-tree
    // исполнителя снимает зависший schtasks
    private static readonly TimeSpan RegisterTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UnregisterTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Конвертирует номер дня недели в аббревиатуру для schtasks.
    /// </summary>
    private static string GetDayName(int day) => day switch
    {
        1 => "MON",
        2 => "TUE",
        3 => "WED",
        4 => "THU",
        5 => "FRI",
        6 => "SAT",
        7 => "SUN",
        _ => "MON"
    };
}
