using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Stats;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для дашборда со статистикой и графиками.
/// </summary>
public partial class DashboardViewModel : ObservableObject
{
    private readonly CleaningStatsService _stats;

    public DashboardViewModel(CleaningStatsService stats)
    {
        _stats = stats;
        Refresh();
    }

    // ─── Summary cards ──────────────────
    [ObservableProperty] private int _totalSessions;
    [ObservableProperty] private string _totalFreed = "0 Б";
    [ObservableProperty] private int _totalFiles;
    [ObservableProperty] private string _averagePerSession = "0 Б";

    // ─── Chart data ─────────────────────
    [ObservableProperty] private List<double>? _monthlyValues;
    [ObservableProperty] private List<string>? _monthlyLabels;
    [ObservableProperty] private List<double>? _weeklyValues;
    [ObservableProperty] private List<string>? _weeklyLabels;

    // ─── View mode ──────────────────────
    [ObservableProperty] private int _chartModeIndex; // 0=Monthly, 1=Weekly

    /// <summary>
    /// Обновляет все данные дашборда.
    /// </summary>
    [RelayCommand]
    private void Refresh()
    {
        TotalSessions = _stats.TotalSessions;
        TotalFreed = SizeFormatter.Format(_stats.TotalFreedBytes);
        TotalFiles = _stats.TotalFilesDeleted;
        AveragePerSession = TotalSessions > 0
            ? SizeFormatter.Format(_stats.TotalFreedBytes / TotalSessions)
            : "0 Б";

        // Monthly chart
        var monthly = _stats.GetMonthlyStats(12);
        MonthlyLabels = monthly.Select(m => m.Label).ToList();
        MonthlyValues = monthly.Select(m => (double)m.TotalBytes).ToList();

        // Weekly chart
        var weekly = _stats.GetWeeklyStats(12);
        WeeklyLabels = weekly.Select(w => w.Label).ToList();
        WeeklyValues = weekly.Select(w => (double)w.TotalBytes).ToList();
    }

    /// <summary>
    /// Очищает историю.
    /// </summary>
    [RelayCommand]
    private void ClearHistory()
    {
        _stats.Clear();
        Refresh();
    }
}
