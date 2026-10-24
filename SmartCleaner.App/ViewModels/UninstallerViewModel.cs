using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
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
    private string _statusText = Localizer.GetString("Uninstaller_Status_PressRefresh");

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
        StatusText = Localizer.GetString("Uninstaller_Status_ReadingRegistry");

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

            StatusText = Localizer.GetString("Uninstaller_Status_Found", TotalAppsCount, TotalSizeFormatted);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Uninstaller_Status_LoadError", ex.Message);
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
        if (app == null || IsLoading) return;

        var result = MessageBox.Show(
            Localizer.GetString("Uninstaller_UninstallConfirmBody", app.DisplayName, app.DisplayVersion, app.Publisher),
            Localizer.GetString("Uninstaller_UninstallConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            StatusText = Localizer.GetString("Uninstaller_Status_Launching", app.DisplayName);
            var (success, msg) = await _engine.UninstallAppAsync(app);

            if (!success)
            {
                // H1: честная причина отказа/кода завершения доходит до пользователя
                MessageBox.Show(msg, Localizer.GetString("Uninstaller_Caption_Uninstall"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Scan for leftovers regardless: даже при ненулевом коде выхода часть файлов могла удалиться
            StatusText = Localizer.GetString("Uninstaller_Status_SearchingLeftovers", app.DisplayName);
            var leftovers = await _hunter.FindLeftoversAsync(app);

            if (leftovers.Count > 0)
            {
                CurrentLeftovers.Clear();
                foreach (var l in leftovers) CurrentLeftovers.Add(l);
                IsLeftoversVisible = true;
                StatusText = Localizer.GetString("Uninstaller_Status_FoundLeftovers", leftovers.Count, app.DisplayName);
            }
            else if (success)
            {
                StatusText = Localizer.GetString("Uninstaller_Status_DoneNoLeftovers");
                AllApps.Remove(app);
                FilteredApps.Remove(app);
                TotalAppsCount = AllApps.Count;
            }
            else
            {
                // Деинсталляция не выполнена: приложение остаётся в списке, показываем причину
                StatusText = msg;
            }
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Uninstaller_Status_UninstallError", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ScanLeftoversOnlyAsync(InstalledAppItem? app)
    {
        if (app == null || IsLoading) return;

        IsLoading = true;
        try
        {
            StatusText = Localizer.GetString("Uninstaller_Status_ScanningLeftovers", app.DisplayName);
            var leftovers = await _hunter.FindLeftoversAsync(app);

            CurrentLeftovers.Clear();
            foreach (var l in leftovers) CurrentLeftovers.Add(l);
            IsLeftoversVisible = true;

            StatusText = leftovers.Count > 0
                ? Localizer.GetString("Uninstaller_Status_FoundLeftoverItems", leftovers.Count, app.DisplayName)
                : Localizer.GetString("Uninstaller_Status_NoLeftovers");
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Uninstaller_Status_LeftoversError", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CleanSelectedLeftoversAsync()
    {
        var selected = CurrentLeftovers.Where(l => l.IsSelected).ToList();
        if (selected.Count == 0 || IsLoading) return;

        IsLoading = true;
        try
        {
            StatusText = Localizer.GetString("Uninstaller_Status_CleaningLeftovers");
            var cleanResult = await _hunter.CleanLeftoversAsync(selected);

            // Из списка убираем только реально удалённые элементы
            foreach (var item in cleanResult.CleanedItems)
            {
                CurrentLeftovers.Remove(item);
            }

            if (CurrentLeftovers.Count == 0)
            {
                IsLeftoversVisible = false;
            }

            StatusText = Localizer.GetString("Uninstaller_Status_LeftoversCleaned", cleanResult.CleanedCount, SizeFormatter.Format(cleanResult.SavedBytes));

            var summary = Localizer.GetString("Uninstaller_LeftoversDoneBody", cleanResult.CleanedCount, SizeFormatter.Format(cleanResult.SavedBytes));
            if (cleanResult.SkippedMessages.Count > 0)
            {
                summary += Localizer.GetString("Uninstaller_LeftoversSkippedPrefix", cleanResult.SkippedMessages.Count)
                           + string.Join("\n", cleanResult.SkippedMessages.Take(10));
                if (cleanResult.SkippedMessages.Count > 10)
                    summary += Localizer.GetString("Uninstaller_LeftoversSkippedMoreSuffix", cleanResult.SkippedMessages.Count - 10);
            }

            MessageBox.Show(summary, Localizer.GetString("Uninstaller_Caption_CleanLeftovers"), MessageBoxButton.OK,
                cleanResult.SkippedMessages.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Uninstaller_Status_CleanLeftoversError", ex.Message);
            MessageBox.Show(
                Localizer.GetString("Uninstaller_Msg_CleanLeftoversFailed", ex.Message),
                Localizer.GetString("Uninstaller_Caption_CleanLeftovers"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CloseLeftovers()
    {
        IsLeftoversVisible = false;
    }
}
