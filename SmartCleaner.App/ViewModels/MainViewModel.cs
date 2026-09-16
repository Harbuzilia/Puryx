using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Scanning;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using SmartCleaner.Core.Services;
using System.Windows;
using System.Windows.Data;

namespace SmartCleaner.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const string CleaningSettingsFilename = "cleaning_settings.json";

    private readonly IEnumerable<IScannerStrategy> _scanners;
    private readonly ICleaningService _cleaningService;
    private readonly IPackageMaintenanceService _packageMaintenanceService;
    private readonly ISafetyService _safetyService;
    private readonly LoggingService _loggingService;
    private readonly IConfigService _configService;
    private readonly CleaningStatsServiceLegacy _statsService;
    private readonly ScanConfiguration _scanConfig;

    /// <summary>Источник отмены текущего сканирования</summary>
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    private bool _isCleaning;

    [ObservableProperty]
    private string _statusText = "Готов к сканированию";

    [ObservableProperty]
    private string? _selectedCategoryName;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private long _selectedSize;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectedSourceFilter = "All";

    [ObservableProperty]
    private string _selectedScopeFilter = "All";

    [ObservableProperty]
    private int _filteredCount;

    /// <summary>Поисковый запрос для фильтрации результатов</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Текущее поле сортировки</summary>
    [ObservableProperty]
    private string _sortField = "Size";

    /// <summary>Направление сортировки (true = по убыванию)</summary>
    [ObservableProperty]
    private bool _sortDescending = true;

    /// <summary>Прогресс очистки (0-100)</summary>
    [ObservableProperty]
    private double _cleaningProgressValue;

    /// <summary>Текст статуса очистки (текущий файл)</summary>
    [ObservableProperty]
    private string _cleaningStatusText = string.Empty;

    /// <summary>Всего освобождено за всё время (форматированно)</summary>
    [ObservableProperty]
    private string _totalFreedAllTime = "0 B";

    /// <summary>Количество сессий очистки</summary>
    [ObservableProperty]
    private int _sessionCount;

    /// <summary>Текст toast-уведомления</summary>
    [ObservableProperty]
    private string _toastMessage = string.Empty;

    /// <summary>Видимость toast-уведомления</summary>
    [ObservableProperty]
    private bool _isToastVisible;

    /// <summary>Категории для отображения в sidebar после сканирования</summary>
    public ObservableCollection<CategorySummary> Categories { get; } = new();

    public ObservableCollection<ScanResult> ScanResults { get; } = new();
    
    public ObservableCollection<ScannedItemViewModel> AllItems { get; } = new();

    /// <summary>Чекбоксы сканеров — пользователь выбирает что искать</summary>
    public ObservableCollection<ScannerOptionViewModel> ScannerOptions { get; } = new();

    /// <summary>Текущий профиль сканирования</summary>
    [ObservableProperty]
    private string _selectedProfile = "Полное";

    /// <summary>Показывать ли панель настроек сканирования</summary>
    [ObservableProperty]
    private bool _isScanConfigVisible;

    /// <summary>Путь дополнительной папки для сканирования</summary>
    [ObservableProperty]
    private string _customScanPath = string.Empty;

    public ICollectionView FilteredItems { get; }

    public IReadOnlyList<string> SourceFilters { get; } = ["All", "Filesystem", "NPM", "Python"];

    public IReadOnlyList<string> ScopeFilters { get; } = ["All", "ProjectLocal", "Global/System"];

    public MainViewModel(
        IEnumerable<IScannerStrategy> scanners,
        ICleaningService cleaningService,
        IPackageMaintenanceService packageMaintenanceService,
        ISafetyService safetyService,
        LoggingService loggingService,
        IConfigService configService,
        CleaningStatsServiceLegacy statsService,
        ScanConfiguration scanConfig)
    {
        _scanners = scanners;
        _cleaningService = cleaningService;
        _packageMaintenanceService = packageMaintenanceService;
        _safetyService = safetyService;
        _loggingService = loggingService;
        _configService = configService;
        _statsService = statsService;
        _scanConfig = scanConfig;

        FilteredItems = CollectionViewSource.GetDefaultView(AllItems);
        FilteredItems.Filter = FilterMatches;
        FilteredItems.GroupDescriptions.Add(new PropertyGroupDescription("CategoryName"));
        AllItems.CollectionChanged += (_, _) => RefreshFilteredItems();

        // Загрузить статистику
        var stats = _statsService.Load();
        TotalFreedAllTime = FormatSize(stats.TotalFreedBytes);
        SessionCount = stats.SessionCount;

        // Инициализировать чекбоксы сканеров
        foreach (var scanner in _scanners.OrderBy(s => s.DisplayOrder))
        {
            ScannerOptions.Add(new ScannerOptionViewModel
            {
                CategoryName = scanner.CategoryName,
                Icon = scanner.CategoryIcon,
                IsEnabled = scanner.IsEnabledByDefault
            });
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "Сканирование...";
        ScanResults.Clear();
        AllItems.Clear();
        Categories.Clear();

        // Передаём CustomScanPath в общую конфигурацию для CustomFolderScanner
        _scanConfig.CustomScanPath = string.IsNullOrWhiteSpace(CustomScanPath) ? null : CustomScanPath;

        // Автоматически включаем/выключаем чекбокс "Выбранная папка"
        var customOption = ScannerOptions.FirstOrDefault(o => o.CategoryName == "Выбранная папка");
        if (customOption != null)
            customOption.IsEnabled = !string.IsNullOrWhiteSpace(CustomScanPath);

        try
        {
            // Фильтруем сканеры по чекбоксам
            var enabledCategories = ScannerOptions
                .Where(o => o.IsEnabled)
                .Select(o => o.CategoryName)
                .ToHashSet();

            var activeScanners = _scanners
                .Where(s => enabledCategories.Contains(s.CategoryName))
                .OrderBy(s => s.DisplayOrder);

            foreach (var scanner in activeScanners)
            {
                _scanCts.Token.ThrowIfCancellationRequested();

                StatusText = $"Сканирование: {scanner.CategoryName}...";

                try
                {
                    // Баг 8: таймаут 60с на каждый сканер — если зависнет (Docker CLI и т.п.), не блокирует всё
                    using var scannerCts = CancellationTokenSource.CreateLinkedTokenSource(_scanCts.Token);
                    scannerCts.CancelAfter(TimeSpan.FromSeconds(60));

                    var result = await scanner.ScanAsync(
                        new Progress<string>(s => StatusText = s),
                        scannerCts.Token);
                    
                    if (result.Items.Count > 0)
                    {
                        ScanResults.Add(result);
                        
                        foreach (var item in result.Items)
                        {
                            var vm = new ScannedItemViewModel(item, result.CategoryName);
                            vm.SelectionChanged = UpdateTotals;
                            AllItems.Add(vm);
                        }
                    }
                }
                catch (OperationCanceledException) when (!_scanCts.Token.IsCancellationRequested)
                {
                    // Таймаут конкретного сканера — пропускаем, не прерываем весь скан
                    StatusText = $"⚠️ {scanner.CategoryName}: таймаут, пропущен";
                    await Task.Delay(500); // Чтобы пользователь увидел сообщение
                }
            }

            BuildCategories();
            RefreshFilteredItems();
            UpdateTotals();
            StatusText = $"Найдено: {FormatSize(TotalSize)} в {TotalCount} элементах";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сканирование отменено";
            BuildCategories();
            RefreshFilteredItems();
            UpdateTotals();
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanScan() => !IsScanning && !IsCleaning;

    /// <summary>
    /// Отмена текущего сканирования
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    /// <summary>
    /// Переключить видимость панели настроек сканирования
    /// </summary>
    [RelayCommand]
    private void ToggleScanConfig()
    {
        IsScanConfigVisible = !IsScanConfigVisible;
    }

    /// <summary>
    /// Применить профиль сканирования — включает/выключает нужные сканеры
    /// </summary>
    [RelayCommand]
    private void ApplyProfile(string profileName)
    {
        SelectedProfile = profileName;

        // Определяем какие категории включить для каждого профиля
        HashSet<string>? enabledSet = profileName switch
        {
            "Быстрая" => ["Система", "Браузеры", "Приложения"],
            "Разработка" => ["Кэши пакетов", "Node Modules", "npm пакеты", "Python пакеты", 
                            "Python окружения", ".NET Артефакты", "Docker", "AI Агенты"],
            "Полное" => null, // null = все включены
            _ => null
        };

        foreach (var option in ScannerOptions)
        {
            option.IsEnabled = enabledSet == null || enabledSet.Contains(option.CategoryName);
        }
    }

    /// <summary>
    /// Выбрать папку для сканирования через системный диалог
    /// </summary>
    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Выберите папку для сканирования"
        };

        if (dialog.ShowDialog() == true)
        {
            CustomScanPath = dialog.FolderName;
        }
    }

    /// <summary>
    /// Событие запроса фокуса на поле поиска (Ctrl+F)
    /// </summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>
    /// Команда фокуса на поле поиска (Ctrl+F)
    /// </summary>
    [RelayCommand]
    private void FocusSearch()
    {
        FocusSearchRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var selectedVMs = AllItems
            .Where(i => i.IsSelected && i.Risk != RiskCategory.UserData)
            .ToList();

        if (selectedVMs.Count == 0)
        {
            StatusText = "Нет выбранных элементов для очистки";
            return;
        }

        // Показываем диалог подтверждения
        var dialogVM = Views.PreviewDialogViewModel.Create(selectedVMs);
        var dialog = new Views.PreviewDialog
        {
            DataContext = dialogVM,
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();

        if (!dialog.Confirmed)
        {
            StatusText = "Очистка отменена";
            return;
        }

        var selectedItems = selectedVMs.Select(i => i.Item).ToList();
        var actionPartition = CleaningActionRouter.Partition(selectedItems);

        if (actionPartition.FileSystemItems.Count > 0)
        {
            ApplyCleaningModeFromSettings();
        }

        IsCleaning = true;
        CleaningProgressValue = 0;
        CleaningStatusText = "Подготовка...";
        StatusText = "Очистка...";

        try
        {
            var fileResult = new CleaningResult();
            if (actionPartition.FileSystemItems.Count > 0)
            {
                fileResult = await _cleaningService.CleanAsync(
                    actionPartition.FileSystemItems,
                    new Progress<CleaningProgress>(p =>
                    {
                        CleaningProgressValue = p.Percentage;
                        CleaningStatusText = $"Очистка: {p.ProcessedCount}/{p.TotalCount} — {System.IO.Path.GetFileName(p.CurrentItem)}";
                        StatusText = $"Очистка файлов: {p.ProcessedCount}/{p.TotalCount} ({p.Percentage:0}%)";
                    }));
            }

            var packageResult = new PackageMaintenanceResult();
            if (actionPartition.PackageItems.Count > 0)
            {
                CleaningStatusText = "Обслуживание пакетов...";
                StatusText = "Обслуживание пакетов...";
                packageResult = await _packageMaintenanceService.ExecuteAsync(
                    actionPartition.PackageItems,
                    new Progress<CleaningProgress>(p =>
                    {
                        CleaningProgressValue = p.Percentage;
                        CleaningStatusText = $"Пакеты: {p.ProcessedCount}/{p.TotalCount} — {p.CurrentItem}";
                        StatusText = $"Обслуживание пакетов: {p.ProcessedCount}/{p.TotalCount} ({p.Percentage:0}%)";
                    }));
            }

            var result = MergeResults(fileResult, packageResult);

            StatusText = BuildCompletionStatus(fileResult, packageResult, result);
            
            // Логируем сессию
            _loggingService.LogCleaningSession(result, selectedItems);

            // Записываем в статистику
            _statsService.RecordSession(result.FreedBytes, result.DeletedCount);
            var updatedStats = _statsService.Load();
            TotalFreedAllTime = FormatSize(updatedStats.TotalFreedBytes);
            SessionCount = updatedStats.SessionCount;
            
            // Удаляем очищенные элементы из списка
            var cleaned = actionPartition.FileSystemItems.Where(i => !PathExists(i.Path)).ToList();
            foreach (var item in cleaned)
            {
                var vm = AllItems.FirstOrDefault(i => i.Path == item.Path);
                if (vm != null)
                    AllItems.Remove(vm);
            }

            var failedPackagePaths = packageResult.Errors
                .Select(error => error.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var completedPackageItems = actionPartition.PackageItems
                .Where(item => !failedPackagePaths.Contains(item.Path))
                .ToList();

            foreach (var item in completedPackageItems)
            {
                var vm = AllItems.FirstOrDefault(i => i.Path == item.Path);
                if (vm != null)
                    AllItems.Remove(vm);
            }

            BuildCategories();
            RefreshFilteredItems();
            UpdateTotals();

            // Toast — показываем и ошибки, если были
            var toastMsg = $"✅ {FormatSize(result.FreedBytes)} освобождено ({result.DeletedCount} элементов)";
            if (result.SkippedCount > 0 || result.FailedCount > 0)
                toastMsg += $"\n⚠️ Пропущено: {result.SkippedCount}, Ошибок: {result.FailedCount}";
            ShowToast(toastMsg);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка очистки: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
            CleaningProgressValue = 0;
            CleaningStatusText = string.Empty;
        }
    }

    private bool CanClean() => !IsScanning && !IsCleaning && SelectedCount > 0;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in AllItems.Where(i => i.Risk == RiskCategory.SafeToDelete && !i.IsLocked))
        {
            item.IsSelected = true;
        }
        UpdateTotals();
    }

    /// <summary>
    /// Выбрать все элементы с риском PerformanceCache (кэши, безопасно удалять)
    /// </summary>
    [RelayCommand]
    private void SelectCache()
    {
        foreach (var item in AllItems.Where(i => 
            (i.Risk == RiskCategory.SafeToDelete || i.Risk == RiskCategory.PerformanceCache) && !i.IsLocked))
        {
            item.IsSelected = true;
        }
        UpdateTotals();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in AllItems)
        {
            item.IsSelected = false;
        }
        UpdateTotals();
    }

    /// <summary>
    /// Выбрать категорию в sidebar для фильтрации результатов.
    /// null означает "Все категории".
    /// </summary>
    [RelayCommand]
    private void SelectCategory(string? categoryName)
    {
        SelectedCategoryName = categoryName;
        RefreshFilteredItems();
    }

    public void UpdateTotals()
    {
        TotalSize = AllItems.Sum(i => i.Size);
        TotalCount = AllItems.Count;
        SelectedSize = AllItems.Where(i => i.IsSelected).Sum(i => i.Size);
        SelectedCount = AllItems.Count(i => i.IsSelected);
        
        CleanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSourceFilterChanged(string value) => RefreshFilteredItems();
    partial void OnSelectedScopeFilterChanged(string value) => RefreshFilteredItems();
    partial void OnSearchTextChanged(string value) => RefreshFilteredItems();

    /// <summary>
    /// Переключить сортировку по указанному полю (Name/Size/Risk).
    /// Повторный клик — переключает направление.
    /// </summary>
    [RelayCommand]
    private void SortBy(string field)
    {
        if (SortField == field)
            SortDescending = !SortDescending;
        else
        {
            SortField = field;
            SortDescending = field == "Size"; // Size по умолчанию desc, остальные asc
        }
        ApplySorting();
    }

    /// <summary>
    /// Открыть папку элемента в Проводнике
    /// </summary>
    [RelayCommand]
    private void OpenFolder(ScannedItemViewModel? item)
    {
        if (item == null) return;
        var folder = item.Item.IsDirectory ? item.Path : System.IO.Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true }); }
            catch (Exception ex) { /* Не критично */ Debug.WriteLine($"[MainViewModel] OpenFolder error: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Скопировать путь элемента в буфер обмена
    /// </summary>
    [RelayCommand]
    private void CopyPath(ScannedItemViewModel? item)
    {
        if (item == null) return;
        try { Clipboard.SetText(item.Path); }
        catch (Exception ex) { /* Не критично */ Debug.WriteLine($"[MainViewModel] CopyPath error: {ex.Message}"); }
    }

    /// <summary>
    /// Добавить путь элемента в whitelist SafetyService
    /// </summary>
    [RelayCommand]
    private void AddToWhitelist(ScannedItemViewModel? item)
    {
        if (item == null) return;
        _safetyService.AddToWhitelist(item.Path);
        AllItems.Remove(item);
        BuildCategories();
        RefreshFilteredItems();
        UpdateTotals();
        StatusText = $"Добавлено в белый список: {System.IO.Path.GetFileName(item.Path)}";
    }

    /// <summary>
    /// Экспорт результатов сканирования в CSV
    /// </summary>
    [RelayCommand]
    private void ExportResults()
    {
        if (AllItems.Count == 0)
        {
            StatusText = "Нечего экспортировать — сначала выполните сканирование";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV файл|*.csv|JSON файл|*.json",
            FileName = $"SmartCleaner_Scan_{DateTime.Now:yyyy-MM-dd_HHmm}",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            if (dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                ExportAsJson(dialog.FileName);
            else
                ExportAsCsv(dialog.FileName);

            StatusText = $"Экспортировано {AllItems.Count} элементов в {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private bool FilterMatches(object obj)
    {
        if (obj is not ScannedItemViewModel item)
            return false;

        return MatchesCategoryFilter(item) && MatchesSourceFilter(item) 
            && MatchesScopeFilter(item) && MatchesSearchText(item);
    }

    /// <summary>
    /// Фильтр по выбранной категории из sidebar
    /// </summary>
    private bool MatchesCategoryFilter(ScannedItemViewModel item)
    {
        if (string.IsNullOrEmpty(SelectedCategoryName))
            return true; // "Все категории"

        return item.CategoryName == SelectedCategoryName;
    }

    private bool MatchesSourceFilter(ScannedItemViewModel item)
    {
        return SelectedSourceFilter switch
        {
            "Filesystem" => item.Item.ActionTarget == CleaningActionTarget.FileSystem,
            "NPM" => item.Item.PackageManager == PackageManagerType.Npm,
            "Python" => item.Item.PackageManager == PackageManagerType.Pip,
            _ => true
        };
    }

    private bool MatchesScopeFilter(ScannedItemViewModel item)
    {
        return SelectedScopeFilter switch
        {
            "ProjectLocal" => item.Item.ActionTarget == CleaningActionTarget.FileSystem || !item.Item.IsReadonlyInventory,
            "Global/System" => item.Item.IsReadonlyInventory,
            _ => true
        };
    }

    /// <summary>
    /// Проверка текстового поиска — ищет в Path, Name, Description, CategoryName, ParentApp
    /// </summary>
    private bool MatchesSearchText(ScannedItemViewModel item)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        var query = SearchText.Trim();
        return item.Path.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (item.ParentApp?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void RefreshFilteredItems()
    {
        FilteredItems.Refresh();
        FilteredCount = FilteredItems.Cast<object>().Count();
    }

    /// <summary>
    /// Применить текущую сортировку к FilteredItems
    /// </summary>
    private void ApplySorting()
    {
        FilteredItems.SortDescriptions.Clear();

        var direction = SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        FilteredItems.SortDescriptions.Add(SortField switch
        {
            "Name" => new SortDescription("Name", direction),
            "Risk" => new SortDescription("Risk", direction),
            _ => new SortDescription("Size", direction)
        });
    }

    private void ExportAsCsv(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Категория,Путь,Размер (байт),Размер,Риск,Описание,Приложение");
        foreach (var item in AllItems)
        {
            sb.AppendLine($"\"{item.CategoryName}\",\"{item.Path}\",{item.Size},\"{item.SizeFormatted}\",\"{item.Risk}\",\"{item.Description}\",\"{item.ParentApp ?? ""}\"");
        }
        // BOM + UTF-8 для корректного открытия в Excel
        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void ExportAsJson(string filePath)
    {
        var data = AllItems.Select(item => new
        {
            category = item.CategoryName,
            path = item.Path,
            size = item.Size,
            sizeFormatted = item.SizeFormatted,
            risk = item.Risk.ToString(),
            description = item.Description,
            parentApp = item.ParentApp ?? ""
        }).ToList();

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// Строит список категорий для sidebar на основе результатов сканирования
    /// </summary>
    private void BuildCategories()
    {
        Categories.Clear();

        var groups = AllItems
            .GroupBy(i => i.CategoryName)
            .Select(g => new CategorySummary
            {
                Name = g.Key,
                Icon = ScanResults.FirstOrDefault(r => r.CategoryName == g.Key)?.CategoryIcon ?? "",
                TotalSize = g.Sum(i => i.Size),
                ItemCount = g.Count(),
                TopItems = g.OrderByDescending(i => i.Size)
                    .Take(3)
                    .Select(i => $"{i.Name} — {i.SizeFormatted}")
                    .ToList()
            })
            .OrderBy(c => _scanners.FirstOrDefault(s => s.CategoryName == c.Name)?.DisplayOrder ?? 999)
            .ToList();

        // Добавляем "Все категории" первым элементом
        Categories.Add(new CategorySummary
        {
            Name = "Все категории",
            Icon = "\uE80F",
            TotalSize = groups.Sum(g => g.TotalSize),
            ItemCount = groups.Sum(g => g.ItemCount),
            IsAll = true
        });

        foreach (var cat in groups)
        {
            Categories.Add(cat);
        }
    }

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);

    private void ApplyCleaningModeFromSettings()
    {
        var settings = _configService.Load<CleaningSettingsConfig>(CleaningSettingsFilename);
        _cleaningService.Mode = settings.UseRecycleBin
            ? CleaningMode.ToRecycleBin
            : CleaningMode.Permanent;
    }

    private static bool PathExists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    private static CleaningResult MergeResults(CleaningResult fileResult, PackageMaintenanceResult packageResult)
    {
        return new CleaningResult
        {
            DeletedCount = fileResult.DeletedCount + packageResult.SucceededCount,
            FreedBytes = fileResult.FreedBytes + packageResult.FreedBytes,
            SkippedCount = fileResult.SkippedCount + packageResult.SkippedCount,
            FailedCount = fileResult.FailedCount + packageResult.FailedCount,
            Errors = fileResult.Errors.Concat(packageResult.Errors).ToList(),
            Duration = fileResult.Duration + packageResult.Duration
        };
    }

    private static string BuildCompletionStatus(
        CleaningResult fileResult,
        PackageMaintenanceResult packageResult,
        CleaningResult mergedResult)
    {
        var hasFileOps = fileResult.DeletedCount > 0 || fileResult.FreedBytes > 0;
        var hasPackageOps = packageResult.ProcessedCount > 0;

        if (hasFileOps && hasPackageOps)
        {
            return $"Выполнено: удалено {fileResult.DeletedCount}, деинсталлировано {packageResult.SucceededCount}, освобождено {FormatSize(mergedResult.FreedBytes)}";
        }

        if (hasPackageOps)
        {
            var packageStatus = $"Пакеты: деинсталлировано {packageResult.SucceededCount}, ошибок {packageResult.FailedCount}, пропущено {packageResult.SkippedCount}";
            return packageResult.FreedBytes > 0
                ? $"{packageStatus}, освобождено {FormatSize(packageResult.FreedBytes)}"
                : packageStatus;
        }

        return $"Очищено: {FormatSize(mergedResult.FreedBytes)} ({mergedResult.DeletedCount} элементов)";
    }

    private CancellationTokenSource? _toastCts;

    /// <summary>
    /// Показать toast-уведомление на 4 секунды (с защитой от гонки)
    /// </summary>
    private async void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts = new CancellationTokenSource();
        var token = _toastCts.Token;

        ToastMessage = message;
        IsToastVisible = true;
        try
        {
            await Task.Delay(4000, token);
            IsToastVisible = false;
        }
        catch (OperationCanceledException) { /* Новый toast отменил старый — ОК */ }
        catch (Exception) { /* async void — не должен ронять приложение */ }
    }

    /// <summary>
    /// Освобождение CancellationTokenSource при закрытии ViewModel
    /// </summary>
    public void Dispose()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Сканировать перетащенные папки/файлы (Drag & Drop)
    /// </summary>
    public async Task ScanDroppedPathsAsync(string[] paths)
    {
        if (IsScanning) return;

        var dirs = paths.Where(Directory.Exists).ToArray();
        if (dirs.Length == 0)
        {
            ShowToast("⚠️ Перетащите папки для сканирования");
            return;
        }

        StatusText = $"Сканирование {dirs.Length} папок...";
        IsScanning = true;
        _scanCts = new CancellationTokenSource();

        try
        {
            AllItems.Clear();
            ScanResults.Clear();
            Categories.Clear();

            // Fix #4: используем чекбоксы сканеров, а не IsEnabledByDefault
            var enabledCategories = ScannerOptions
                .Where(o => o.IsEnabled)
                .Select(o => o.CategoryName)
                .ToHashSet();

            var activeScanners = _scanners
                .Where(s => enabledCategories.Contains(s.CategoryName))
                .OrderBy(s => s.DisplayOrder);

            foreach (var scanner in activeScanners)
            {
                _scanCts.Token.ThrowIfCancellationRequested();

                // Fix #5: таймаут 60с как в основном ScanAsync
                using var scannerCts = CancellationTokenSource.CreateLinkedTokenSource(_scanCts.Token);
                scannerCts.CancelAfter(TimeSpan.FromSeconds(60));

                ScanResult result;
                try
                {
                    var progress = new Progress<string>(msg => StatusText = msg);
                    result = await scanner.ScanAsync(progress, scannerCts.Token);
                }
                catch (OperationCanceledException) when (!_scanCts.Token.IsCancellationRequested)
                {
                    StatusText = $"⚠️ {scanner.CategoryName}: таймаут, пропущен";
                    await Task.Delay(500);
                    continue;
                }

                // Фильтруем только элементы из указанных папок
                var filtered = result.Items
                    .Where(item => dirs.Any(d => item.Path.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (filtered.Count > 0)
                {
                    ScanResults.Add(result);
                    foreach (var item in filtered)
                    {
                        AllItems.Add(new ScannedItemViewModel(item, result.CategoryName)
                        {
                            SelectionChanged = UpdateTotals
                        });
                    }
                }
            }

            BuildCategories();
            RefreshFilteredItems();
            UpdateTotals();
            ApplySorting();

            StatusText = $"Найдено {AllItems.Count} элементов в {dirs.Length} папках";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Сканирование отменено";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }
}

public partial class ScannedItemViewModel : ObservableObject
{
    public ScannedItem Item { get; }
    public string CategoryName { get; }
    public Action? SelectionChanged { get; set; }

    public string Path => Item.Path;
    public string Name => System.IO.Path.GetFileName(Item.Path);
    public long Size => Item.Size;
    public string SizeFormatted => FormatSize(Item.Size);
    public RiskCategory Risk => Item.Risk;
    public string Description => Item.Description;
    public string? Explanation => Item.Explanation;
    public string? HowToRestore => Item.HowToRestore;
    public bool IsLocked => Item.IsLocked;
    public string? RestoreCommand => Item.RestoreCommand;

    /// <summary>
    /// Формирует текст для всплывающей подсказки с полной информацией.
    /// </summary>
    public string ToolTipText
    {
        get
        {
            var parts = new List<string> { Description };
            if (!string.IsNullOrEmpty(Explanation))
                parts.Add($"\n📖 {Explanation}");
            if (!string.IsNullOrEmpty(HowToRestore))
                parts.Add($"\n🔄 {HowToRestore}");
            if (!string.IsNullOrEmpty(RestoreCommand))
                parts.Add($"\n💻 Команда: {RestoreCommand}");
            return string.Join("", parts);
        }
    }
    public string? ParentApp => Item.ParentApp;

    [ObservableProperty]
    private bool _isSelected;

    public ScannedItemViewModel(ScannedItem item, string categoryName)
    {
        Item = item;
        CategoryName = categoryName;
        _isSelected = item.IsSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }

    private static string FormatSize(long bytes) => SizeFormatter.Format(bytes);
}

/// <summary>
/// Чекбокс сканера — пользователь может включить/выключить каждый тип сканирования
/// </summary>
public partial class ScannerOptionViewModel : ObservableObject
{
    /// <summary>Имя категории сканера (CategoryName из IScannerStrategy)</summary>
    public required string CategoryName { get; init; }

    /// <summary>Иконка сканера (Segoe MDL2)</summary>
    public required string Icon { get; init; }

    /// <summary>Включён ли сканер</summary>
    [ObservableProperty]
    private bool _isEnabled = true;
}
