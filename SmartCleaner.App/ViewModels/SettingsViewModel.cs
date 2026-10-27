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
using SmartCleaner.Core.Update;
using SmartCleaner.Core.Localization;
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
    private readonly IUpdateChecker _updateChecker;

    private readonly IUpdateInstaller _updateInstaller;

    private readonly TrayService _trayService;

    [ObservableProperty]
    private bool _useRecycleBin = true;

    [ObservableProperty]
    private int _protectedHours = 24;

    [ObservableProperty]
    private int _selectedThemeIndex;

    public IReadOnlyList<string> ThemeOptions { get; } =
    [
        Localizer.GetString("Settings_ThemeSystem"),
        Localizer.GetString("Settings_ThemeDark"),
        Localizer.GetString("Settings_ThemeLight")
    ];

    // ─── Язык интерфейса (ROADMAP v3.1, фаза L) ───

    private const string UiSettingsFilename = "ui_settings.json";

    [ObservableProperty]
    private int _selectedLanguageIndex;

    /// <summary>
    /// Варианты языка: «Авто» (локализован), «Русский»/«English» —
    /// самоназвания языков, не переводятся.
    /// </summary>
    public IReadOnlyList<string> LanguageOptions { get; } =
    [
        SmartCleaner.Core.Localization.Localizer.GetString("Settings_LanguageAuto"),
        "Русский",
        "English"
    ];

    /// <summary>
    /// Запрос показать toast «перезапустите для смены языка» — подписывается
    /// MainWindow (toast живёт в MainViewModel).
    /// </summary>
    public event Action? LanguageRestartHintRequested;

    // ─── Режим сканирования: MFT (ROADMAP v3.1, День 9, фаза M) ───

    /// <summary>Единая точка режима MFT — имя файла Core (MftDiskScanner).</summary>
    private const string ScanSettingsFilename = SmartCleaner.Core.Mft.MftDiskScanner.SettingsFilename;

    [ObservableProperty]
    private int _selectedMftModeIndex;

    /// <summary>
    /// Варианты режима MFT: Авто (NTFS + права → MFT, иначе фолбэк) | Вкл |
    /// Выкл. Единая настройка scan_settings.json — не хардкодится в сканеры.
    /// </summary>
    public IReadOnlyList<string> MftModeOptions { get; } =
    [
        Localizer.GetString("Settings_MftModeAuto"),
        Localizer.GetString("Settings_MftModeOn"),
        Localizer.GetString("Settings_MftModeOff")
    ];

    // ─── Cookie-файлы браузеров (ROADMAP v3.1, День 14, фаза B) ───

    /// <summary>
    /// Искать cookie-хранилища браузеров (RiskCategory.UserData, opt-in).
    /// По умолчанию выключено: без согласия сканер не выдаёт cookie-элементы вообще;
    /// при включении они никогда не выбираются автоматически, а удаление требует
    /// дополнительного подтверждения. Хранится в общем scan_settings.json.
    /// </summary>
    [ObservableProperty]
    private bool _browserCleanCookies;

    // ─── Deep-pass системного мусора (ROADMAP v3.1, День 15, фаза F) ───

    /// <summary>
    /// Глубокий поиск <c>.chk</c>/<c>.tmp</c>/<c>.dmp</c> в ограниченных мусорных
    /// корнях (%TEMP%, Windows\Temp, WER/Minidump, FOUND.*). По умолчанию
    /// выключено: расширенный проход по подкаталогам — только по явному
    /// согласию. Хранится в общем scan_settings.json.
    /// </summary>
    [ObservableProperty]
    private bool _deepJunkScan;

    // ─── Обновления (ROADMAP v3.1, День 11, фаза U) ───

    /// <summary>
    /// Статус последней проверки обновлений (локализован; пусто до первой
    /// проверки). День 12: статус-строка ведёт весь флоу — проверка →
    /// подтверждение → скачивание с прогрессом → верификация → рестарт.
    /// </summary>
    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Checking");

        var result = await _updateChecker.CheckAsync();

        if (result.ErrorKey is not null)
        {
            UpdateStatusText = Localizer.GetString(result.ErrorKey);
            return;
        }

        if (!result.HasUpdate)
        {
            UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_UpToDate", result.LatestVersion);
            return;
        }

        UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Available", result.LatestVersion, result.CurrentVersion);

        // День 12 (ROADMAP v3.1): диалог подтверждения — установка только
        // по явному согласию; сеть после этого затрагивается один раз
        var confirmation = MessageBox.Show(
            Localizer.GetString("Settings_UpdateConfirmText", result.LatestVersion),
            Localizer.GetString("Settings_UpdateConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            // Прогресс репортится в UI-поток (SyncContext захвачен при создании Progress)
            var progress = new Progress<double>(fraction =>
                UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Downloading", fraction));
            var download = await _updateInstaller.DownloadAsync(result, progress);
            if (!download.Success)
            {
                UpdateStatusText = Localizer.GetString(download.ErrorKey ?? "Update_DownloadFailed");
                return;
            }

            UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Verifying");
            if (!_updateInstaller.VerifySha256(download.TempFilePath, result.Sha256))
            {
                UpdateStatusText = Localizer.GetString("Update_HashMismatch");
                return;
            }

            // Успешный swap завершает процесс внутри ApplyUpdate — сюда
            // возвращаемся только при честном отказе (ключ семейства Update_*)
            UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Restarting");
            var apply = _updateInstaller.ApplyUpdate(download.TempFilePath);
            UpdateStatusText = Localizer.GetString(apply.ErrorKey ?? "Update_ApplyFailed");
        }
        catch (OperationCanceledException)
        {
            // Отмена скачивания: partial-файл в temp уже вычищен инсталлером
            UpdateStatusText = Localizer.GetString("Settings_UpdateStatus_Canceled");
        }
    }

    // ─── Трей (ROADMAP v3.1, День 13, фаза A) ───

    /// <summary>
    /// Сворачивать окно в трей по кнопке «свернуть». Закрытие окна (X) всегда
    /// завершает приложение — одна опция, один смысл (docs/TRAY-MODE.md).
    /// </summary>
    [ObservableProperty]
    private bool _minimizeToTray;

    /// <summary>
    /// Фоновая проверка обновлений не чаще раза в сутки (opt-in): только балун
    /// в трее, без авто-скачивания и установки. Отметка последней проверки
    /// хранится в <see cref="UpdateStateConfig.Filename"/>.
    /// </summary>
    [ObservableProperty]
    private bool _autoCheckUpdates;

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
        RamOptimizerService ramOptimizer,
        IUpdateChecker updateChecker,
        IUpdateInstaller updateInstaller,
        TrayService trayService)
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
        _updateChecker = updateChecker;
        _updateInstaller = updateInstaller;
        _trayService = trayService;

        _protectedHours = (int)_safetyService.ProtectedPeriod.TotalHours;
        _selectedThemeIndex = _themeManager.CurrentTheme switch
        {
            AppTheme.Dark => 1,
            AppTheme.Light => 2,
            _ => 0
        };

        // Прямое поле (не свойство) — загрузка не триггерит OnChanged/сохранение
        _selectedLanguageIndex = _configService.Load<UiSettingsConfig>(UiSettingsFilename).Language switch
        {
            "ru" => 1,
            "en" => 2,
            _ => 0
        };

        // Режим MFT и cookie-opt-in — единый scan_settings.json; прямыми полями
        // (MftDiskScanner нормализует значения, загрузка не должна сохранять файл)
        var scanSettings = _configService.Load<ScanSettingsConfig>(ScanSettingsFilename);
        _selectedMftModeIndex = scanSettings.MftScanMode switch
        {
            "On" => 1,
            "Off" => 2,
            _ => 0
        };
        _browserCleanCookies = scanSettings.BrowserCleanCookies;
        _deepJunkScan = scanSettings.DeepJunkScan;

        // Трей — прямым полем: загрузка не триггерит OnChanged/сохранение
        _minimizeToTray = _configService.Load<TraySettingsConfig>(TraySettingsConfig.Filename).MinimizeToTray;
        _autoCheckUpdates = _configService.Load<TraySettingsConfig>(TraySettingsConfig.Filename).AutoCheckUpdates;

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
                    Label = Localizer.GetString("Settings_DriveLabel", drive.Name, drive.VolumeLabel, free, total),
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

    partial void OnSelectedLanguageIndexChanged(int value)
    {
        // Язык применяется при следующем старте: сохраняем настройку и
        // подсказываем перезапуском (toast — на языке текущей сессии)
        SaveLanguageSetting(value switch
        {
            1 => "ru",
            2 => "en",
            _ => "Auto"
        });
        LanguageRestartHintRequested?.Invoke();
    }

    private void SaveLanguageSetting(string language)
    {
        // Read-modify-write: ui_settings.json общий с темой — как в ThemeManager.SaveTheme
        var settings = _configService.Load<UiSettingsConfig>(UiSettingsFilename);
        settings.Language = language;
        _configService.Save(UiSettingsFilename, settings);
    }

    partial void OnSelectedMftModeIndexChanged(int value)
    {
        // Read-modify-write: scan_settings.json — единая точка режима MFT,
        // файл читается MftDiskScanner'ом в Core при каждом скане
        var settings = _configService.Load<ScanSettingsConfig>(ScanSettingsFilename);
        settings.MftScanMode = value switch
        {
            1 => "On",
            2 => "Off",
            _ => "Auto"
        };
        _configService.Save(ScanSettingsFilename, settings);
    }

    partial void OnBrowserCleanCookiesChanged(bool value)
    {
        // Read-modify-write: scan_settings.json читается BrowserScanner'ом в Core
        // при каждом скане; MFT-режим в том же файле не теряется
        var settings = _configService.Load<ScanSettingsConfig>(ScanSettingsFilename);
        settings.BrowserCleanCookies = value;
        _configService.Save(ScanSettingsFilename, settings);
    }

    partial void OnDeepJunkScanChanged(bool value)
    {
        // Read-modify-write: scan_settings.json читается SystemScanner'ом в Core
        // при каждом скане; MFT-режим и cookie-opt-in в том же файле не теряются
        var settings = _configService.Load<ScanSettingsConfig>(ScanSettingsFilename);
        settings.DeepJunkScan = value;
        _configService.Save(ScanSettingsFilename, settings);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        var settings = _configService.Load<TraySettingsConfig>(TraySettingsConfig.Filename);
        settings.MinimizeToTray = value;
        _configService.Save(TraySettingsConfig.Filename, settings);
        // Применение на лету: перезапуск для трей-опции не нужен
        _trayService.ApplyOptions(value, _autoCheckUpdates);
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        var settings = _configService.Load<TraySettingsConfig>(TraySettingsConfig.Filename);
        settings.AutoCheckUpdates = value;
        _configService.Save(TraySettingsConfig.Filename, settings);
        // Таймер авто-проверки взводится/снимается сразу, без перезапуска
        _trayService.ApplyOptions(_minimizeToTray, value);
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
            if (!ok) MessageBox.Show(msg, Localizer.GetString("Settings_Caption_ExplorerIntegration"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (!ok) MessageBox.Show(msg, Localizer.GetString("Settings_Caption_ExplorerIntegration"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            if (!ok) MessageBox.Show(msg, Localizer.GetString("Settings_Caption_ExplorerIntegration"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                TotalFreedFormatted = Localizer.GetString("Settings_Report_AnalysisComplete")
            };

            await _reportGenerator.GenerateAndOpenReportAsync(reportData);
            _audioService.PlayBoostActivated();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.GetString("Settings_Status_ReportError", ex.Message), Localizer.GetString("Settings_Caption_Report"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            Title = Localizer.GetString("Common_SelectFolderForScan")
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
