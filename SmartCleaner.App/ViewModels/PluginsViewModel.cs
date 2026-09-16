using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Helpers;
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
    private string _statusText = "Нажмите «Сканировать плагины» для аудита установленных расширений";

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
        StatusText = "Загрузка плагинов и манифестов...";

        try
        {
            var list = await _engine.LoadPluginsAsync();
            Plugins.Clear();
            foreach (var p in list) Plugins.Add(p);
            PluginsCount = Plugins.Count;

            StatusText = "Сканирование объектов плагинов...";
            var items = await _engine.ScanPluginItemsAsync(Plugins);
            ScanItems.Clear();
            long total = 0;
            foreach (var i in items)
            {
                ScanItems.Add(i);
                total += i.SizeBytes;
            }

            TotalCleanableSize = SizeFormatter.Format(total);
            StatusText = $"Загружено {PluginsCount} плагинов. Найдено объектов для очистки на {TotalCleanableSize}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
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
            $"Очистить {selected.Count} элементов, обнаруженных плагинами сообщества?",
            "Очистка по правилам плагинов",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = "Выполняется очистка плагинов...";

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
                ? $"\nПропущено (политика безопасности): {cleanResult.SkippedMessages.Count}."
                : string.Empty;

            StatusText = $"Успешно очищено {cleanResult.CleanedCount} элементов (Освобождено: {SizeFormatter.Format(cleanResult.SavedBytes)})!";
            MessageBox.Show(
                $"Очищено {cleanResult.CleanedCount} объектов ({SizeFormatter.Format(cleanResult.SavedBytes)}).{skippedNote}",
                "Очистка завершена", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
