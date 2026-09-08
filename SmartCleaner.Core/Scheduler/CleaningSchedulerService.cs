using System.Diagnostics;
using System.Text.Json;

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
/// </summary>
public sealed class CleaningSchedulerService
{
    private const string TaskName = "SmartCleaner_AutoClean";
    private readonly string _settingsPath;

    public CleaningSchedulerService()
    {
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
    /// Регистрирует или обновляет задачу в Windows Task Scheduler.
    /// Возвращает true при успехе.
    /// </summary>
    public bool RegisterTask(ScheduleSettings settings)
    {
        try
        {
            // Сначала удалим существующую задачу
            UnregisterTask();

            if (!settings.IsEnabled) return true;

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            // Формируем расписание для schtasks
            var scheduleType = settings.Interval switch
            {
                "Daily" => "DAILY",
                "Monthly" => "MONTHLY",
                _ => "WEEKLY"
            };

            var startTime = $"{settings.Hour:D2}:{settings.Minute:D2}";

            var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --auto-clean --profile {settings.Profile}\" " +
                       $"/SC {scheduleType} /ST {startTime} /F /RL LIMITED";

            if (scheduleType == "WEEKLY")
                args += $" /D {GetDayName(settings.DayOfWeek)}";

            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(10000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Register error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Удаляет задачу из Windows Task Scheduler.
    /// </summary>
    public bool UnregisterTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{TaskName}\" /F",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return true; // OK даже если задачи не было
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Unregister error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Проверяет, зарегистрирована ли задача.
    /// </summary>
    public bool IsTaskRegistered()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            return proc?.ExitCode == 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[Scheduler] Query error: {ex.Message}"); return false; }
    }

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
