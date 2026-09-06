using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Scheduler;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для настроек планировщика очистки.
/// </summary>
public partial class SchedulerViewModel : ObservableObject
{
    private readonly CleaningSchedulerService _scheduler;

    public SchedulerViewModel(CleaningSchedulerService scheduler)
    {
        _scheduler = scheduler;
        LoadSettings();
    }

    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private int _selectedIntervalIndex; // 0=Daily, 1=Weekly, 2=Monthly
    [ObservableProperty] private int _selectedDayOfWeek; // 0=Mon..6=Sun (index)
    [ObservableProperty] private int _hour = 3;
    [ObservableProperty] private int _minute;
    [ObservableProperty] private int _selectedProfileIndex; // 0=Быстрая, 1=Разработка, 2=Полное
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isTaskRegistered;
    [ObservableProperty] private string _lastRunText = "Нет данных";

    /// <summary>
    /// Ограничивает час диапазоном 0-23.
    /// </summary>
    partial void OnHourChanged(int value)
    {
        if (value < 0) Hour = 0;
        else if (value > 23) Hour = 23;
    }

    /// <summary>
    /// Ограничивает минуты диапазоном 0-59.
    /// </summary>
    partial void OnMinuteChanged(int value)
    {
        if (value < 0) Minute = 0;
        else if (value > 59) Minute = 59;
    }

    public string[] Intervals { get; } = ["Ежедневно", "Еженедельно", "Ежемесячно"];
    public string[] DaysOfWeek { get; } = ["Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье"];
    public string[] Profiles { get; } = ["Быстрая", "Разработка", "Полное"];

    /// <summary>
    /// Загружает настройки из файла и обновляет UI.
    /// </summary>
    private void LoadSettings()
    {
        var s = _scheduler.LoadSettings();
        IsEnabled = s.IsEnabled;
        SelectedIntervalIndex = s.Interval switch { "Daily" => 0, "Monthly" => 2, _ => 1 };
        SelectedDayOfWeek = Math.Clamp(s.DayOfWeek - 1, 0, 6);
        Hour = s.Hour;
        Minute = s.Minute;
        SelectedProfileIndex = s.Profile switch { "Разработка" => 1, "Полное" => 2, _ => 0 };
        LastRunText = s.LastRun?.ToString("dd.MM.yyyy HH:mm") ?? "Нет данных";
        IsTaskRegistered = _scheduler.IsTaskRegistered();
        StatusText = IsTaskRegistered ? "✅ Задача зарегистрирована в планировщике" : "Задача не зарегистрирована";
    }

    /// <summary>
    /// Сохраняет настройки и регистрирует/убирает задачу.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        var settings = new ScheduleSettings
        {
            IsEnabled = IsEnabled,
            Interval = SelectedIntervalIndex switch { 0 => "Daily", 2 => "Monthly", _ => "Weekly" },
            DayOfWeek = SelectedDayOfWeek + 1,
            Hour = Hour,
            Minute = Minute,
            Profile = Profiles[SelectedProfileIndex]
        };

        _scheduler.SaveSettings(settings);

        if (_scheduler.RegisterTask(settings))
        {
            IsTaskRegistered = IsEnabled;
            StatusText = IsEnabled
                ? "✅ Расписание сохранено и задача зарегистрирована!"
                : "ℹ️ Планировщик отключён, задача удалена.";
        }
        else
        {
            StatusText = "⚠️ Ошибка регистрации задачи. Попробуйте от имени администратора.";
        }
    }

    /// <summary>
    /// Удаляет задачу из планировщика.
    /// </summary>
    [RelayCommand]
    private void RemoveTask()
    {
        IsEnabled = false;
        _scheduler.UnregisterTask();
        _scheduler.SaveSettings(new ScheduleSettings { IsEnabled = false });
        IsTaskRegistered = false;
        StatusText = "🗑️ Задача удалена из планировщика.";
    }
}
