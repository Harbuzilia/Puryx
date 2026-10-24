using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Safety;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class QuarantineViewModel : ObservableObject
{
    private readonly QuarantineService _quarantine;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Localizer.GetString("Quarantine_Status_Ready");

    [ObservableProperty]
    private int _quarantinedCount;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    public ObservableCollection<QuarantinedItem> Items { get; } = new();

    public QuarantineViewModel(QuarantineService quarantine)
    {
        _quarantine = quarantine;
    }

    [RelayCommand]
    public async Task LoadItemsAsync()
    {
        IsBusy = true;
        try
        {
            var list = await _quarantine.GetQuarantinedItemsAsync();
            Items.Clear();
            long totalBytes = 0;
            foreach (var item in list)
            {
                Items.Add(item);
                totalBytes += item.SizeBytes;
            }

            QuarantinedCount = Items.Count;
            TotalSizeFormatted = SizeFormatter.Format(totalBytes);
            StatusText = Localizer.GetString("Quarantine_Status_Items", QuarantinedCount, TotalSizeFormatted);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Quarantine_Status_LoadError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestoreItemAsync(QuarantinedItem? item)
    {
        if (item == null) return;

        var result = MessageBox.Show(
            Localizer.GetString("Quarantine_RestoreConfirmBody", item.Name, item.OriginalPath),
            Localizer.GetString("Quarantine_RestoreConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var (success, msg) = await _quarantine.RestoreItemAsync(item.Id);

            if (success)
            {
                Items.Remove(item);
                QuarantinedCount = Items.Count;
                MessageBox.Show(msg, Localizer.GetString("Quarantine_Caption_Restored"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(msg, Localizer.GetString("Quarantine_Caption_RestoreError"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_RestoreError", ex.Message);
            MessageBox.Show(ex.Message, Localizer.GetString("Quarantine_Caption_RestoreError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task PurgeItemAsync(QuarantinedItem? item)
    {
        if (item == null) return;

        var result = MessageBox.Show(
            Localizer.GetString("Quarantine_PurgeConfirmBody", item.Name),
            Localizer.GetString("Quarantine_PurgeConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            var (success, message) = await _quarantine.PurgeItemAsync(item.Id);

            if (success)
            {
                Items.Remove(item);
                QuarantinedCount = Items.Count;
            }
            else
            {
                MessageBox.Show(message, Localizer.GetString("Quarantine_Caption_DeleteError"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Quarantine_Status_DeleteError", ex.Message);
            MessageBox.Show(ex.Message, Localizer.GetString("Quarantine_Caption_DeleteError"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
