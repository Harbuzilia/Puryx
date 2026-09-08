using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.DiskHealth;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Optimization;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.SystemOpt;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class RamOptimizerViewModel : ObservableObject
{
    private readonly RamOptimizerService _ramService;
    private readonly SqliteCompactorService _sqliteService;
    private readonly FileShredderService _shredderService;
    private readonly GameBoostService _gameBoostService;
    private readonly DiskHealthService _diskHealthService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Готов к анализу и оптимизации оперативной памяти, баз данных и SSD";

    [ObservableProperty]
    private string _ramTotal = "0 GB";

    [ObservableProperty]
    private string _ramUsed = "0 GB";

    [ObservableProperty]
    private string _ramAvailable = "0 GB";

    [ObservableProperty]
    private double _ramLoadPercentage;

    [ObservableProperty]
    private string _reclaimedRamText = "";

    [ObservableProperty]
    private string _shredFilePath = "";

    // Game Boost State
    [ObservableProperty]
    private bool _isGameBoostActive;

    [ObservableProperty]
    private string _gameBoostStatusText = "Турбо-режим не активен";

    public ObservableCollection<SqliteDbTarget> Databases { get; } = new();
    public ObservableCollection<PhysicalDiskInfo> PhysicalDisks { get; } = new();

    public RamOptimizerViewModel(
        RamOptimizerService ramService,
        SqliteCompactorService sqliteService,
        FileShredderService shredderService,
        GameBoostService gameBoostService,
        DiskHealthService diskHealthService)
    {
        _ramService = ramService;
        _sqliteService = sqliteService;
        _shredderService = shredderService;
        _gameBoostService = gameBoostService;
        _diskHealthService = diskHealthService;

        RefreshRamMetrics();
        _ = LoadDisksHealthAsync();
    }

    [RelayCommand]
    public void RefreshRamMetrics()
    {
        var mem = _ramService.GetMemoryStatus();
        RamTotal = mem.TotalFormatted;
        RamUsed = mem.UsedFormatted;
        RamAvailable = mem.AvailableFormatted;
        RamLoadPercentage = mem.LoadPercentage;
    }

    [RelayCommand]
    public async Task OptimizeRamAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Оптимизация оперативной памяти (Working Sets)...";

        try
        {
            var res = await _ramService.OptimizeMemoryAsync();
            RefreshRamMetrics();

            ReclaimedRamText = $"Освобождено: {res.ReclaimedFormatted} в {res.ProcessesOptimized} процессах";
            StatusText = $"Оптимизация RAM завершена! Высвобождено {res.ReclaimedFormatted}.";
            MessageBox.Show($"Оперативная память оптимизирована!\n\nОсвобождено: {res.ReclaimedFormatted}\nОптимизировано процессов: {res.ProcessesOptimized}", "RAM Optimizer", MessageBoxButton.OK, MessageBoxImage.Information);
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
    public async Task ToggleGameBoostAsync()
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            if (!IsGameBoostActive)
            {
                StatusText = "Включение Game Turbo Boost...";
                var state = await _gameBoostService.EnableGameBoostAsync(new Progress<string>(s => StatusText = s));
                IsGameBoostActive = state.IsBoostActive;
                GameBoostStatusText = $"🚀 Game Boost активен! (Остановлено служб: {state.StoppedServices.Count}, RAM: {state.ReclaimedMemoryFormatted})";
                RefreshRamMetrics();
                MessageBox.Show($"Game Turbo Boost активирован!\n\n• Оперативная память очищена\n• Фоновые службы телеметрии/SysMain приостановлены\n• Схема электропитания: Высокая производительность", "Game Turbo Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = "Отключение Game Turbo Boost...";
                var state = await _gameBoostService.DisableGameBoostAsync(new Progress<string>(s => StatusText = s));
                IsGameBoostActive = state.IsBoostActive;
                GameBoostStatusText = "Турбо-режим не активен";
                RefreshRamMetrics();
                MessageBox.Show("Стандартный режим системы восстановлен.", "Game Turbo Boost", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
    public async Task LoadDisksHealthAsync()
    {
        try
        {
            var disks = await _diskHealthService.GetPhysicalDisksHealthAsync();
            PhysicalDisks.Clear();
            foreach (var d in disks) PhysicalDisks.Add(d);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RamOptimizerViewModel] LoadDisksHealth error: {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ScanDatabasesAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Поиск фрагментированных баз данных SQLite (Cursor, VS Code, Chrome)...";

        try
        {
            var list = await _sqliteService.DiscoverDatabasesAsync();
            Databases.Clear();
            foreach (var db in list) Databases.Add(db);

            StatusText = $"Найдено {Databases.Count} баз данных для оптимизации.";
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
    public async Task CompactDatabasesAsync()
    {
        if (Databases.Count == 0 || IsBusy) return;

        IsBusy = true;
        StatusText = "Выполняется VACUUM и сжатие баз данных...";

        try
        {
            var (count, saved) = await _sqliteService.CompactDatabasesAsync(Databases);
            StatusText = $"Успешно оптимизировано {count} баз (Освобождено: {SizeFormatter.Format(saved)})!";
            MessageBox.Show($"Оптимизировано {count} баз данных.\nВысвобождено места: {SizeFormatter.Format(saved)}", "SQLite Compactor", MessageBoxButton.OK, MessageBoxImage.Information);
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
    public async Task ShredFileAsync()
    {
        if (string.IsNullOrWhiteSpace(ShredFilePath) || IsBusy) return;

        var result = MessageBox.Show(
            $"ВНИМАНИЕ! Вы собираетесь БЕЗВОЗВРАТНО уничтожить файл по военному стандарту DoD 5220.22-M:\n\n{ShredFilePath}\n\nФайл будет перезаписан нулями и случайными байтами в 3 прохода. Восстановить его будет невозможно!",
            "Шредер файлов DoD 5220.22-M",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = "Безвозвратное уничтожение файла (DoD 5220.22-M 3-Pass)...";

        try
        {
            var (ok, msg) = await _shredderService.ShredFileAsync(ShredFilePath, ShredMethod.DoD522022M3Pass);
            StatusText = msg;
            MessageBox.Show(msg, "Шредер файлов", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok) ShredFilePath = "";
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
