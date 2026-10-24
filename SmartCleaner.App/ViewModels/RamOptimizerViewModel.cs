using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.DiskHealth;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
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
    private string _statusText = Localizer.GetString("RamOptimizer_Status_Ready");

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
    private string _gameBoostStatusText = Localizer.GetString("RamOptimizer_Status_GameBoostOff");

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
        StatusText = Localizer.GetString("RamOptimizer_Status_OptimizingRam");

        try
        {
            var res = await _ramService.OptimizeMemoryAsync();
            RefreshRamMetrics();

            ReclaimedRamText = Localizer.GetString("RamOptimizer_Status_Reclaimed", res.ReclaimedFormatted, res.ProcessesOptimized);
            StatusText = Localizer.GetString("RamOptimizer_Status_RamDone", res.ReclaimedFormatted);
            MessageBox.Show(Localizer.GetString("RamOptimizer_Msg_RamDone", res.ReclaimedFormatted, res.ProcessesOptimized), Localizer.GetString("RamOptimizer_Caption_Ram"), MessageBoxButton.OK, MessageBoxImage.Information);
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
    public async Task ToggleGameBoostAsync()
    {
        if (IsBusy) return;

        IsBusy = true;

        try
        {
            if (!IsGameBoostActive)
            {
                StatusText = Localizer.GetString("RamOptimizer_Status_GameBoostEnabling");
                var state = await _gameBoostService.EnableGameBoostAsync(new Progress<string>(s => StatusText = s));
                IsGameBoostActive = state.IsBoostActive;
                GameBoostStatusText = Localizer.GetString("RamOptimizer_Status_GameBoostActive", state.StoppedServices.Count, state.ReclaimedMemoryFormatted);
                RefreshRamMetrics();
                MessageBox.Show(Localizer.GetString("RamOptimizer_Msg_GameBoostOn"), Localizer.GetString("RamOptimizer_Caption_GameBoost"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = Localizer.GetString("RamOptimizer_Status_GameBoostDisabling");
                var state = await _gameBoostService.DisableGameBoostAsync(new Progress<string>(s => StatusText = s));
                IsGameBoostActive = state.IsBoostActive;
                GameBoostStatusText = Localizer.GetString("RamOptimizer_Status_GameBoostOff");
                RefreshRamMetrics();
                MessageBox.Show(Localizer.GetString("RamOptimizer_Msg_GameBoostOff"), Localizer.GetString("RamOptimizer_Caption_GameBoost"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
        StatusText = Localizer.GetString("RamOptimizer_Status_ScanningDbs");

        try
        {
            var list = await _sqliteService.DiscoverDatabasesAsync();
            Databases.Clear();
            foreach (var db in list) Databases.Add(db);

            StatusText = Localizer.GetString("RamOptimizer_Status_DbsFound", Databases.Count);
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
    public async Task CompactDatabasesAsync()
    {
        if (Databases.Count == 0 || IsBusy) return;

        IsBusy = true;
        StatusText = Localizer.GetString("RamOptimizer_Status_CompactingDbs");

        try
        {
            var (count, saved) = await _sqliteService.CompactDatabasesAsync(Databases);
            StatusText = Localizer.GetString("RamOptimizer_Status_DbsCompacted", count, SizeFormatter.Format(saved));
            MessageBox.Show(Localizer.GetString("RamOptimizer_Msg_DbsCompacted", count, SizeFormatter.Format(saved)), Localizer.GetString("RamOptimizer_Caption_Sqlite"), MessageBoxButton.OK, MessageBoxImage.Information);
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
    public async Task ShredFileAsync()
    {
        if (string.IsNullOrWhiteSpace(ShredFilePath) || IsBusy) return;

        var result = MessageBox.Show(
            Localizer.GetString("RamOptimizer_Confirm_ShredBody", ShredFilePath),
            Localizer.GetString("RamOptimizer_Caption_Shredder"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = Localizer.GetString("RamOptimizer_Status_Shredding");

        try
        {
            var (ok, msg) = await _shredderService.ShredFileAsync(ShredFilePath, ShredMethod.DoD522022M3Pass);
            StatusText = msg;
            MessageBox.Show(msg, Localizer.GetString("RamOptimizer_TabShredder"), MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (ok) ShredFilePath = "";
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
