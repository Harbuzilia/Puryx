using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
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
    private string _statusText = "Управление изолированными объектами и точками отката";

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
            StatusText = $"В карантине: {QuarantinedCount} элементов ({TotalSizeFormatted})";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка загрузки: {ex.Message}";
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
            $"Восстановить элемент '{item.Name}' на его исходное место:\n\n{item.OriginalPath}?",
            "Восстановление из карантина",
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
                MessageBox.Show(msg, "Восстановлено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(msg, "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка восстановления: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка восстановления", MessageBoxButton.OK, MessageBoxImage.Error);
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
            $"Удалить элемент '{item.Name}' из карантина НАВСЕГДА без возможности восстановления?",
            "Удаление навсегда",
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
                MessageBox.Show(message, "Ошибка удаления",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка удаления: {ex.Message}";
            MessageBox.Show(ex.Message, "Ошибка удаления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
