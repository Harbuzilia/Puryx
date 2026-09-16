using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.App.Services;
using SmartCleaner.Core.DiskHealth;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Reporting;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;
using SmartCleaner.Core.Shell;
using SmartCleaner.Core.SystemOpt;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string ScanPathsFilename = "scan_paths.json";
    private const string CleaningSettingsFilename = "cleaning_settings.json";

    private readonly ISafetyService _safetyService;
    private readonly IConfigService _configService;
    private readonly ContextMenuService _contextMenuService;
    private readonly ThemeManager _themeManager;
    private readonly ExplorerContextMenuManager _shellManager;
    private readonly AudioFeedbackService _audioService;
    private readonly SystemReportGenerator _reportGenerator;
    private readonly DiskHealthService _diskHealthService;
    private readonly RamOptimizerService _ramOptimizer;

    [ObservableProperty]
    private bool _useRecycleBin = true;

    [ObservableProperty]
    private int _protectedHours = 24;

    [ObservableProperty]
    private int _selectedThemeIndex;

    public IReadOnlyList<string> ThemeOptions { get; } = ["Системная", "Тёмная", "Светлая"];

    [ObservableProperty]
    private string _newWhitelistPattern = string.Empty;

    [ObservableProperty]
    private string _newScanPath = string.Empty;

    // Shell Context Menu Toggles
    [ObservableProperty]
    private bool _isAnalyzeMenuEnabled;

    [ObservableProperty]
    private bool _isShredMenuEnabled;

    [ObservableProperty]
    private bool _isCompactMenuEnabled;

    // Sound FX Toggle
    [ObservableProperty]
    private bool _isSoundEnabled;

    public ObservableCollection<string> WhitelistPatterns { get; } = new();
    public ObservableCollection<string> CustomScanPaths { get; } = new();
    public ObservableCollection<DriveInfoViewModel> AvailableDrives { get; } = new();

    public string ConfigModeText => _configService.Mode == Core.Services.ConfigMode.Portable ? "Portable" : "Installed";
    public string ConfigPath => _configService.ConfigDirectory;

    public SettingsViewModel(
        ISafetyService safetyService, 
        IConfigService configService, 
        ContextMenuService contextMenuService,
        ThemeManager themeManager,
        ExplorerContextMenuManager shellManager,
        AudioFeedbackService audioService,
        SystemReportGenerator reportGenerator,
        DiskHealthService diskHealthService,
        RamOptimizerService ramOptimizer)
    {
        _safetyService = safetyService;
        _configService = configService;
        _contextMenuService = contextMenuService;
        _themeManager = themeManager;
        _shellManager = shellManager;
        _audioService = audioService;
        _reportGenerator = reportGenerator;
        _diskHealthService = diskHealthService;
        _ramOptimizer = ramOptimizer;

        _protectedHours = (int)_safetyService.ProtectedPeriod.TotalHours;
        _selectedThemeIndex = _themeManager.CurrentTheme switch
        {
            AppTheme.Dark => 1,
            AppTheme.Light => 2,
            _ => 0
        };

        _isSoundEnabled = _audioService.IsSoundEnabled;
        _isAnalyzeMenuEnabled = _shellManager.IsAnalyzeFolderRegistered();
        _isShredMenuEnabled = _shellManager.IsShredderRegistered();
        _isCompactMenuEnabled = _shellManager.IsCompactRegistered();

        LoadSettings();
    }

    private void LoadSettings()
    {
        WhitelistPatterns.Clear();
        foreach (var pattern in _safetyService.GetWhitelistPatterns())
        {
            WhitelistPatterns.Add(pattern);
        }

        var scanPaths = _configService.Load<ScanPathsConfig>(ScanPathsFilename);
        CustomScanPaths.Clear();
        foreach (var path in scanPaths.Paths)
        {
            CustomScanPaths.Add(path);
        }

        LoadAvailableDrives();
        LoadCleaningSettings();
    }

    private void LoadAvailableDrives()
    {
        AvailableDrives.Clear();
        try
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                var total = FormatSize(drive.TotalSize);
                var free = FormatSize(drive.AvailableFreeSpace);
                AvailableDrives.Add(new DriveInfoViewModel
                {
                    Path = drive.RootDirectory.FullName,
                    Label = $"{drive.Name} ({drive.VolumeLabel}) - Свободно {free} из {total}",
                    IsSelected = true
                });
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SettingsViewModel] LoadAvailableDrives error: {ex.Message}"); }
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _themeManager.CurrentTheme = value switch
        {
            1 => AppTheme.Dark,
            2 => AppTheme.Light,
            _ => AppTheme.System
        };
    }

    partial void OnIsSoundEnabledChanged(bool value)
    {
        _audioService.IsSoundEnabled = value;
    }

    [RelayCommand]
    public void ToggleAnalyzeMenu()
    {
        if (IsAnalyzeMenuEnabled)
        {
            var (ok, msg) = _shellManager.RegisterAnalyzeFolder();
            if (!ok) MessageBox.Show(msg, "Интеграция в Проводник", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            _shellManager.UnregisterAnalyzeFolder();
        }
    }

    [RelayCommand]
    public void ToggleShredMenu()
    {
        if (IsShredMenuEnabled)
        {
            var (ok, msg) = _shellManager.RegisterShredder();
            if (!ok) MessageBox.Show(msg, "Интеграция в Проводник", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            _shellManager.UnregisterShredder();
        }
    }

    [RelayCommand]
    public void ToggleCompactMenu()
    {
        if (IsCompactMenuEnabled)
        {
            var (ok, msg) = _shellManager.RegisterCompact();
            if (!ok) MessageBox.Show(msg, "Интеграция в Проводник", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            _shellManager.UnregisterCompact();
        }
    }

    [RelayCommand]
    public async Task GenerateSystemReportAsync()
    {
        try
        {
            var disks = await _diskHealthService.GetPhysicalDisksHealthAsync();
            // Физическая RAM через GlobalMemoryStatusEx; GC-хип — только как fallback
            var totalRam = _ramOptimizer.GetMemoryStatus().TotalBytes;
            var reportData = new SystemReportData
            {
                Disks = disks,
                TotalRamBytes = totalRam > 0
                    ? totalRam
                    : (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                TotalFreedFormatted = "Анализ системы завершен"
            };

            await _reportGenerator.GenerateAndOpenReportAsync(reportData);
            _audioService.PlayBoostActivated();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка генерации отчета: {ex.Message}", "Отчет системы", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void AddPattern()
    {
        if (string.IsNullOrWhiteSpace(NewWhitelistPattern)) return;
        if (!WhitelistPatterns.Contains(NewWhitelistPattern))
        {
            _safetyService.AddToWhitelist(NewWhitelistPattern);
            WhitelistPatterns.Add(NewWhitelistPattern);
        }
        NewWhitelistPattern = string.Empty;
    }

    [RelayCommand]
    private void RemovePattern(string pattern)
    {
        _safetyService.RemoveFromWhitelist(pattern);
        WhitelistPatterns.Remove(pattern);
    }

    [RelayCommand]
    private void AddScanPath()
    {
        if (string.IsNullOrWhiteSpace(NewScanPath)) return;
        if (Directory.Exists(NewScanPath) && !CustomScanPaths.Contains(NewScanPath))
        {
            CustomScanPaths.Add(NewScanPath);
            SaveScanPaths();
        }
        NewScanPath = string.Empty;
    }

    [RelayCommand]
    private void RemoveScanPath(string path)
    {
        CustomScanPaths.Remove(path);
        SaveScanPaths();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для сканирования"
        };

        if (dialog.ShowDialog() == true)
        {
            NewScanPath = dialog.FolderName;
            AddScanPath();
        }
    }

    private void SaveScanPaths()
    {
        var config = new ScanPathsConfig { Paths = CustomScanPaths.ToList() };
        _configService.Save(ScanPathsFilename, config);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _safetyService.ProtectedPeriod = TimeSpan.FromHours(ProtectedHours);
        _safetyService.SaveWhitelist();
        SaveScanPaths();
        SaveCleaningSettings();
    }

    partial void OnProtectedHoursChanged(int value)
    {
        _safetyService.ProtectedPeriod = TimeSpan.FromHours(value);
    }

    partial void OnUseRecycleBinChanged(bool value)
    {
        SaveCleaningSettings();
    }

    private void LoadCleaningSettings()
    {
        var settings = _configService.Load<CleaningSettingsConfig>(CleaningSettingsFilename);
        UseRecycleBin = settings.UseRecycleBin;
    }

    private void SaveCleaningSettings()
    {
        _configService.Save(CleaningSettingsFilename, new CleaningSettingsConfig
        {
            UseRecycleBin = UseRecycleBin
        });
    }

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}

public class DriveInfoViewModel
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsSelected { get; set; }
}
