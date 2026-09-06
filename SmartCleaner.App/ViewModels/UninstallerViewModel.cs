using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Uninstaller;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class UninstallerViewModel : ObservableObject
{
    private readonly UninstallerEngine _engine;
    private readonly LeftoverHunter _hunter;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "Нажмите «Обновить список» для загрузки установленных приложений";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private InstalledAppItem? _selectedApp;

    [ObservableProperty]
    private int _totalAppsCount;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    [ObservableProperty]
    private bool _isLeftoversVisible;

    public ObservableCollection<InstalledAppItem> AllApps { get; } = new();
    public ObservableCollection<InstalledAppItem> FilteredApps { get; } = new();
    public ObservableCollection<LeftoverItem> CurrentLeftovers { get; } = new();

    public UninstallerViewModel(UninstallerEngine engine, LeftoverHunter hunter)
    {
        _engine = engine;
        _hunter = hunter;
    }

    [RelayCommand]
    public async Task LoadAppsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusText = "Чтение реестра и списка установленных программ...";

        try
        {
            var apps = await _engine.ScanInstalledAppsAsync();

            AllApps.Clear();
            long totalBytes = 0;
            foreach (var app in apps)
            {
                AllApps.Add(app);
                totalBytes += app.EstimatedSizeBytes;
            }

            TotalAppsCount = AllApps.Count;
            TotalSizeFormatted = SizeFormatter.Format(totalBytes);
            ApplyFilter();

            StatusText = $"Найдено {TotalAppsCount} установленных приложений ({TotalSizeFormatted})";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки приложений: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = AllApps.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(a =>
                a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.InstallLocation.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredApps.Clear();
        foreach (var item in filtered)
        {
            FilteredApps.Add(item);
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(InstalledAppItem? app)
    {
        if (app == null) return;

        var result = MessageBox.Show(
            $"Запустить деинсталляцию приложения:\n\n{app.DisplayName}\nВерсия: {app.DisplayVersion}\nИздатель: {app.Publisher}\n\nПосле завершения будет выполнен автоматический поиск оставшихся файлов и записей реестра («хвостов»).",
            "Деинсталляция программы",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        StatusText = $"Запуск деинсталлятора для {app.DisplayName}...";
        var (success, msg) = await _engine.UninstallAppAsync(app);

        // Scan for leftovers regardless
        StatusText = $"Поиск остаточных файлов («хвостов») для {app.DisplayName}...";
        var leftovers = await _hunter.FindLeftoversAsync(app);

        if (leftovers.Count > 0)
        {
            CurrentLeftovers.Clear();
            foreach (var l in leftovers) CurrentLeftovers.Add(l);
            IsLeftoversVisible = true;
            StatusText = $"Найдено {leftovers.Count} остаточных файлов/ключей реестра для {app.DisplayName}";
        }
        else
        {
            StatusText = $"Деинсталляция завершена. Хвостов не обнаружено.";
            AllApps.Remove(app);
            FilteredApps.Remove(app);
            TotalAppsCount = AllApps.Count;
        }
    }

    [RelayCommand]
    private async Task ScanLeftoversOnlyAsync(InstalledAppItem? app)
    {
        if (app == null) return;

        StatusText = $"Поиск хвостов для {app.DisplayName}...";
        var leftovers = await _hunter.FindLeftoversAsync(app);

        CurrentLeftovers.Clear();
        foreach (var l in leftovers) CurrentLeftovers.Add(l);
        IsLeftoversVisible = true;

        StatusText = leftovers.Count > 0
            ? $"Найдено {leftovers.Count} остаточных элементов для {app.DisplayName}"
            : $"Остаточных файлов и записей реестра не найдено.";
    }

    [RelayCommand]
    private async Task CleanSelectedLeftoversAsync()
    {
        var selected = CurrentLeftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0) return;

        var (count, space) = await _hunter.CleanLeftoversAsync(selected);

        foreach (var item in selected)
        {
            CurrentLeftovers.Remove(item);
        }

        if (CurrentLeftovers.Count == 0)
        {
            IsLeftoversVisible = false;
        }

        StatusText = $"Успешно вычищено {count} остаточных элементов (Освобождено: {SizeFormatter.Format(space)})!";
        MessageBox.Show($"Очищено {count} элементов ({SizeFormatter.Format(space)}).", "Очистка хвостов", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void CloseLeftovers()
    {
        IsLeftoversVisible = false;
    }
}
