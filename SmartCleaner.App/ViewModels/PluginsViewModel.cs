using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Plugins;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class PluginsViewModel : ObservableObject
{
    private readonly PluginEngine _engine;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Localizer.GetString("Plugins_Status_PressScan");

    [ObservableProperty]
    private int _pluginsCount;

    [ObservableProperty]
    private string _totalCleanableSize = "0 B";

    public ObservableCollection<PluginManifest> Plugins { get; } = new();
    public ObservableCollection<PluginScanItem> ScanItems { get; } = new();

    public PluginsViewModel(PluginEngine engine)
    {
        _engine = engine;
    }

    [RelayCommand]
    public async Task LoadAndScanAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = Localizer.GetString("Plugins_Status_Loading");

        try
        {
            var list = await _engine.LoadPluginsAsync();
            Plugins.Clear();
            foreach (var p in list) Plugins.Add(p);
            PluginsCount = Plugins.Count;

            StatusText = Localizer.GetString("Plugins_Status_Scanning");
            var items = await _engine.ScanPluginItemsAsync(Plugins);
            ScanItems.Clear();
            long total = 0;
            foreach (var i in items)
            {
                ScanItems.Add(i);
                total += i.SizeBytes;
            }

            TotalCleanableSize = SizeFormatter.Format(total);
            StatusText = Localizer.GetString("Plugins_Status_Loaded", PluginsCount, TotalCleanableSize);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanSelectedAsync()
    {
        var selected = ScanItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0) return;

        var result = MessageBox.Show(
            Localizer.GetString("Plugins_Confirm_CleanBody", selected.Count),
            Localizer.GetString("Plugins_Caption_Clean"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = Localizer.GetString("Plugins_Status_Cleaning");

        try
        {
            var cleanResult = await _engine.CleanPluginItemsAsync(selected);
            foreach (var item in cleanResult.CleanedItems)
            {
                ScanItems.Remove(item);
            }

            long remaining = ScanItems.Sum(i => i.SizeBytes);
            TotalCleanableSize = SizeFormatter.Format(remaining);

            var skippedNote = cleanResult.SkippedMessages.Count > 0
                ? Localizer.GetString("Plugins_Status_SkippedSuffix", cleanResult.SkippedMessages.Count)
                : string.Empty;

            StatusText = Localizer.GetString("Plugins_Status_CleanedOk", cleanResult.CleanedCount, SizeFormatter.Format(cleanResult.SavedBytes));
            MessageBox.Show(
                Localizer.GetString("Plugins_Msg_CleanDone", cleanResult.CleanedCount, SizeFormatter.Format(cleanResult.SavedBytes), skippedNote),
                Localizer.GetString("Common_Caption_CleanDone"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
