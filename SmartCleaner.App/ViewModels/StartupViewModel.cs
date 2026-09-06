using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Startup;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для страницы анализа автозагрузки.
/// Показывает все элементы автозагрузки из реестра, папок, планировщика.
/// </summary>
public partial class StartupViewModel : ObservableObject
{
    private readonly StartupEngine _engine;

    public StartupViewModel(StartupEngine engine)
    {
        _engine = engine;
    }

    public ObservableCollection<StartupItemViewModel> Items { get; } = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "Нажмите «Обновить» для сканирования";
    [ObservableProperty] private StartupItemViewModel? _selectedItem;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _enabledCount;

    /// <summary>
    /// Фильтр: показывать все / только включённые / только отключённые.
    /// </summary>
    [ObservableProperty] private int _filterIndex; // 0=все, 1=включённые, 2=отключённые

    partial void OnFilterIndexChanged(int value) => ApplyFilter();

    private List<StartupItem> _allItems = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Сканирование автозагрузки...";

        try
        {
            _allItems = await Task.Run(() => _engine.ScanAll());

            TotalCount = _allItems.Count;
            EnabledCount = _allItems.Count(i => i.IsEnabled);
            StatusText = $"Найдено {TotalCount} элементов ({EnabledCount} активных)";

            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleItem(StartupItemViewModel? itemVm)
    {
        if (itemVm == null) return;

        bool success;
        if (itemVm.IsEnabled)
            success = _engine.DisableItem(itemVm.Item);
        else
            success = _engine.EnableItem(itemVm.Item);

        if (success)
        {
            itemVm.IsEnabled = itemVm.Item.IsEnabled;
            EnabledCount = _allItems.Count(i => i.IsEnabled);
            StatusText = $"{(itemVm.IsEnabled ? "✅ Включено" : "⛔ Отключено")}: {itemVm.Name}";
        }
        else
        {
            StatusText = $"⚠️ Ошибка: не удалось изменить «{itemVm.Name}». Требуются права администратора?";
        }
    }

    [RelayCommand]
    private void DeleteItem(StartupItemViewModel? itemVm)
    {
        if (itemVm == null) return;

        if (_engine.DeleteItem(itemVm.Item))
        {
            Items.Remove(itemVm);
            _allItems.Remove(itemVm.Item);
            TotalCount = _allItems.Count;
            EnabledCount = _allItems.Count(i => i.IsEnabled);
            StatusText = $"🗑️ Удалено: {itemVm.Name}";
        }
        else
        {
            StatusText = $"⚠️ Ошибка удаления «{itemVm.Name}». Требуются права администратора?";
        }
    }

    /// <summary>
    /// Применяет фильтр к списку элементов.
    /// </summary>
    private void ApplyFilter()
    {
        Items.Clear();
        var filtered = FilterIndex switch
        {
            1 => _allItems.Where(i => i.IsEnabled),
            2 => _allItems.Where(i => !i.IsEnabled),
            _ => _allItems.AsEnumerable()
        };

        foreach (var item in filtered.OrderBy(i => i.Source).ThenBy(i => i.Name))
            Items.Add(new StartupItemViewModel(item));
    }
}

/// <summary>
/// ViewModel-обёртка для элемента автозагрузки.
/// </summary>
public partial class StartupItemViewModel : ObservableObject
{
    public StartupItemViewModel(StartupItem item)
    {
        Item = item;
        Name = item.Name;
        Command = item.Command;
        SourceDescription = item.SourceDescription;
        Publisher = string.IsNullOrWhiteSpace(item.Publisher) ? "—" : item.Publisher;
        IsEnabled = item.IsEnabled;
    }

    public StartupItem Item { get; }
    public string Name { get; }
    public string Command { get; }
    public string SourceDescription { get; }
    public string Publisher { get; }
    [ObservableProperty] private bool _isEnabled;

    /// <summary>
    /// Иконка статуса.
    /// </summary>
    public string StatusIcon => IsEnabled ? "✅" : "⛔";
}
