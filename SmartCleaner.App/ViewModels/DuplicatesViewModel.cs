using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartCleaner.Core.Duplicates;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для страницы поиска дубликатов.
/// Три режима + лог удалений + превью файлов + прогресс-бар.
/// </summary>
public partial class DuplicatesViewModel : ObservableObject
{
    private readonly DuplicateEngine _engine;
    private readonly DeletionLogService _deletionLog;
    private readonly ISafetyService _safety;
    private CancellationTokenSource? _cts;

    public DuplicatesViewModel(DuplicateEngine engine, DeletionLogService deletionLog, ISafetyService safety)
    {
        _engine = engine;
        _deletionLog = deletionLog;
        _safety = safety;
        LoadDeletionHistory();
    }

    // ─── Общее состояние ────────────────────────

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Готов к сканированию";
    [ObservableProperty] private int _selectedTabIndex;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private bool _isProgressIndeterminate;

    // ─── Режим 1: Поиск дубликатов ──────────────

    [ObservableProperty] private string _scanFolders = "";
    [ObservableProperty] private bool _optBySize = true;
    [ObservableProperty] private bool _optByHash = true;
    [ObservableProperty] private bool _optByName;
    [ObservableProperty] private bool _optByByte;
    [ObservableProperty] private bool _optTurbo = true;
    [ObservableProperty] private string _excludePatterns = ".git\nnode_modules\n*.tmp";
    [ObservableProperty] private string _minFileSizeText = "1024";

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

    [ObservableProperty] private int _totalGroups;
    [ObservableProperty] private int _totalDuplicates;
    [ObservableProperty] private string _totalWasted = "";

    // ─── Режим 2: Поиск по образцу ──────────────

    [ObservableProperty] private string _sampleFilePath = "";
    [ObservableProperty] private string _sampleSearchFolders = "";

    public ObservableCollection<DuplicateFileViewModel> SampleResults { get; } = [];

    // ─── Режим 3: Сравнение папок ───────────────

    [ObservableProperty] private string _compareFolderA = "";
    [ObservableProperty] private string _compareFolderB = "";
    [ObservableProperty] private int _uniqueACount;
    [ObservableProperty] private int _uniqueBCount;
    [ObservableProperty] private int _commonCount;

    public ObservableCollection<CompareItemViewModel> CompareResults { get; } = [];

    // ─── Режим 4: Лог удалений ─────────────────

    public ObservableCollection<DeletionSessionViewModel> DeletionHistory { get; } = [];

    // ─── Превью файла ──────────────────────────

    [ObservableProperty] private DuplicateFileViewModel? _selectedPreviewFile;
    [ObservableProperty] private string _previewMode = "none"; // "image", "text", "info", "none"
    [ObservableProperty] private BitmapImage? _previewImage;
    [ObservableProperty] private string _previewText = "";
    [ObservableProperty] private string _previewInfo = "";

    private CancellationTokenSource? _previewCts;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".ico", ".tiff", ".tif"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cs", ".py", ".js", ".ts", ".json", ".xml", ".html", ".css",
        ".md", ".log", ".cfg", ".ini", ".yml", ".yaml", ".toml", ".bat", ".sh",
        ".sql", ".csv", ".java", ".cpp", ".c", ".h", ".rb", ".go", ".rs"
    };

    /// <summary>
    /// При смене выбранного файла загружает превью.
    /// </summary>
    partial void OnSelectedPreviewFileChanged(DuplicateFileViewModel? value)
    {
        _previewCts?.Cancel();

        if (value == null)
        {
            PreviewMode = "none";
            PreviewImage = null;
            PreviewText = "";
            PreviewInfo = "";
            return;
        }

        _ = LoadPreviewAsync(value);
    }

    /// <summary>
    /// Загружает превью в фоне, чтобы файловый IO не блокировал UI-поток.
    /// Токен отменяет устаревшую загрузку при быстрой смене выбора.
    /// </summary>
    private async Task LoadPreviewAsync(DuplicateFileViewModel file)
    {
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        try
        {
            var ext = Path.GetExtension(file.Path);

            if (ImageExtensions.Contains(ext))
            {
                var image = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(file.Path, UriKind.Absolute);
                        bi.DecodePixelWidth = 400;
                        bi.EndInit();
                        bi.Freeze();
                        return bi;
                    }
                    catch
                    {
                        return null;
                    }
                }, token);

                if (image != null)
                {
                    token.ThrowIfCancellationRequested();
                    PreviewImage = image;
                    PreviewMode = "image";
                    return;
                }
            }
            else if (TextExtensions.Contains(ext))
            {
                var text = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        // Ограничиваем до 10KB для производительности
                        using var reader = new StreamReader(file.Path);
                        var buffer = new char[10240];
                        int read = reader.Read(buffer, 0, buffer.Length);
                        var content = new string(buffer, 0, read);
                        if (read == buffer.Length) content += "\n\n... (обрезано до 10 КБ) ...";
                        return content;
                    }
                    catch
                    {
                        return null;
                    }
                }, token);

                if (text != null)
                {
                    token.ThrowIfCancellationRequested();
                    PreviewText = text;
                    PreviewMode = "text";
                    return;
                }
            }

            await ShowInfoPreviewAsync(file, token);
        }
        catch (OperationCanceledException)
        {
            // Выбор уже сменился — результат этой загрузки не нужен
        }
    }

    /// <summary>
    /// Показывает базовую информацию о файле.
    /// </summary>
    private async Task ShowInfoPreviewAsync(DuplicateFileViewModel file, CancellationToken token)
    {
        var info = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var fi = new FileInfo(file.Path);
                return $"📁 {fi.Name}\n" +
                       $"Размер: {SizeFormatter.Format(fi.Length)}\n" +
                       $"Создан: {fi.CreationTime:dd.MM.yyyy HH:mm}\n" +
                       $"Изменён: {fi.LastWriteTime:dd.MM.yyyy HH:mm}\n" +
                       $"Расширение: {fi.Extension}\n" +
                       $"Атрибуты: {fi.Attributes}\n" +
                       $"Путь: {fi.FullName}";
            }
            catch (Exception ex)
            {
                return $"Ошибка: {ex.Message}";
            }
        }, token);

        token.ThrowIfCancellationRequested();
        PreviewInfo = info;
        PreviewMode = "info";
    }

    // ────────────────────────────────────────────
    //  КОМАНДЫ
    // ────────────────────────────────────────────

    [RelayCommand]
    private void BrowseScanFolders()
    {
        var path = PickFolder("Выберите папку для сканирования дубликатов");
        if (path != null)
        {
            ScanFolders = string.IsNullOrWhiteSpace(ScanFolders)
                ? path
                : ScanFolders + "\n" + path;
        }
    }

    [RelayCommand]
    private void BrowseSampleFile()
    {
        var dlg = new OpenFileDialog { Title = "Выберите файл-образец" };
        if (dlg.ShowDialog() == true)
            SampleFilePath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseSampleSearchFolders()
    {
        var path = PickFolder("Выберите папку для поиска копий");
        if (path != null)
        {
            SampleSearchFolders = string.IsNullOrWhiteSpace(SampleSearchFolders)
                ? path
                : SampleSearchFolders + "\n" + path;
        }
    }

    [RelayCommand]
    private void BrowseFolderA()
    {
        var path = PickFolder("Выберите папку A");
        if (path != null) CompareFolderA = path;
    }

    [RelayCommand]
    private void BrowseFolderB()
    {
        var path = PickFolder("Выберите папку B");
        if (path != null) CompareFolderB = path;
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
        StatusText = "Отмена...";
    }

    // ─── Поиск дубликатов ───────────────────────

    [RelayCommand]
    private async Task StartDuplicateScanAsync()
    {
        var dirs = ParsePaths(ScanFolders);
        if (dirs.Count == 0) { StatusText = "⚠️ Укажите хотя бы одну папку"; return; }

        IsScanning = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        DuplicateGroups.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            long.TryParse(MinFileSizeText, out var minSize);
            var options = new DuplicateScanOptions
            {
                BySize = OptBySize,
                ByHash = OptByHash,
                ByName = OptByName,
                ByByte = OptByByte,
                TurboMode = OptTurbo,
                MinFileSize = Math.Max(1, minSize),
                ExcludePatterns = ParsePatterns(ExcludePatterns)
            };

            var progress = new Progress<string>(msg => StatusText = msg);
            var percentProgress = new Progress<int>(p =>
            {
                ProgressPercent = p;
                IsProgressIndeterminate = false;
            });
            var groups = await _engine.ScanForDuplicatesAsync(dirs, options, progress, _cts.Token, percentProgress);

            foreach (var g in groups)
                DuplicateGroups.Add(new DuplicateGroupViewModel(g));

            TotalGroups = groups.Count;
            TotalDuplicates = groups.Sum(g => g.Files.Count - 1);
            TotalWasted = SizeFormatter.Format(groups.Sum(g => g.WastedBytes));
            ProgressPercent = 100;
            StatusText = $"Найдено {TotalGroups} групп ({TotalDuplicates} дубликатов, {TotalWasted} потрачено впустую)";
        }
        catch (OperationCanceledException) { StatusText = "Сканирование отменено"; }
        catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
        finally { IsScanning = false; IsProgressIndeterminate = false; _cts?.Dispose(); _cts = null; }
    }

    // ─── Поиск по образцу ───────────────────────

    [RelayCommand]
    private async Task StartSampleScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SampleFilePath) || !File.Exists(SampleFilePath))
        { StatusText = "⚠️ Файл-образец не найден"; return; }

        var dirs = ParsePaths(SampleSearchFolders);
        if (dirs.Count == 0) { StatusText = "⚠️ Укажите папки для поиска"; return; }

        IsScanning = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        SampleResults.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            var options = new DuplicateScanOptions
            {
                BySize = OptBySize,
                ByHash = OptByHash,
                ByName = OptByName,
                TurboMode = OptTurbo
            };

            var progress = new Progress<string>(msg => StatusText = msg);
            var found = await _engine.ScanForSampleAsync(SampleFilePath, dirs, options, progress, _cts.Token);

            foreach (var f in found)
                SampleResults.Add(new DuplicateFileViewModel(f));

            ProgressPercent = 100;
            IsProgressIndeterminate = false;
            StatusText = $"Найдено {found.Count} копий файла «{Path.GetFileName(SampleFilePath)}»";
        }
        catch (OperationCanceledException) { StatusText = "Поиск отменён"; }
        catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
        finally { IsScanning = false; IsProgressIndeterminate = false; _cts?.Dispose(); _cts = null; }
    }

    // ─── Сравнение папок ────────────────────────

    [RelayCommand]
    private async Task StartCompareFoldersAsync()
    {
        if (!Directory.Exists(CompareFolderA)) { StatusText = "⚠️ Папка A не существует"; return; }
        if (!Directory.Exists(CompareFolderB)) { StatusText = "⚠️ Папка B не существует"; return; }

        IsScanning = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        CompareResults.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var result = await _engine.CompareFoldersAsync(CompareFolderA, CompareFolderB, progress, _cts.Token);

            UniqueACount = result.UniqueA.Count;
            UniqueBCount = result.UniqueB.Count;
            CommonCount = result.Common.Count;

            foreach (var f in result.UniqueA)
                CompareResults.Add(new CompareItemViewModel("Только A", f.Path, SizeFormatter.Format(f.Size), "—", "—"));

            foreach (var f in result.UniqueB)
                CompareResults.Add(new CompareItemViewModel("Только B", f.Path, "—", SizeFormatter.Format(f.Size), "—"));

            foreach (var c in result.Common)
            {
                var status = c.Similarity >= 1.0 ? "Идентичны"
                    : c.Similarity >= 0.5 ? "Размер совпал, содержимое нет"
                    : "Различны";
                CompareResults.Add(new CompareItemViewModel(
                    status, c.RelativePath,
                    SizeFormatter.Format(c.FileA.Size),
                    SizeFormatter.Format(c.FileB.Size),
                    c.Newer));
            }

            ProgressPercent = 100;
            IsProgressIndeterminate = false;
            StatusText = $"A: {UniqueACount} уникальных, B: {UniqueBCount} уникальных, общих: {CommonCount}";
        }
        catch (OperationCanceledException) { StatusText = "Сравнение отменено"; }
        catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
        finally { IsScanning = false; IsProgressIndeterminate = false; _cts?.Dispose(); _cts = null; }
    }

    // ─── Удаление выбранных дубликатов ──────────

    [RelayCommand]
    private async Task DeleteSelectedDuplicatesAsync()
    {
        var selected = DuplicateGroups
            .SelectMany(g => g.Files.Where(f => f.IsSelected))
            .ToList();

        if (selected.Count == 0)
        {
            StatusText = "⚠️ Выберите файлы для удаления";
            return;
        }

        // Safety-гейт: whitelist, блокировки, права
        var allowed = new List<(DuplicateFileViewModel File, long Size)>();
        int blocked = 0;
        string? firstBlockReason = null;

        foreach (var file in selected)
        {
            var validation = _safety.ValidateForDeletion(new ScannedItem
            {
                Path = file.Path,
                Size = file.Size,
                Risk = RiskCategory.PerformanceCache,
                Description = "duplicate:manual"
            });

            if (validation.CanDelete && !validation.RequiresElevation)
            {
                allowed.Add((file, file.Size));
            }
            else
            {
                blocked++;
                firstBlockReason ??= validation.BlockReason ?? "Требуются права администратора";
            }
        }

        int deleted = 0;
        int errors = 0;
        var deletedPaths = new List<(string Path, long Size)>();

        await Task.Run(() =>
        {
            foreach (var (file, size) in allowed)
            {
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        file.Path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    deleted++;
                    deletedPaths.Add((file.Path, size));
                }
                catch (Exception ex) { Debug.WriteLine($"[DuplicatesViewModel] Delete file error: {ex.Message}"); errors++; }
            }
        });

        // Лог фиксирует только фактически удалённые файлы
        _deletionLog.LogDeletion(deletedPaths);

        StatusText = $"Удалено {deleted} файлов в корзину"
            + (blocked > 0 ? $", заблокировано {blocked} ({firstBlockReason})" : "")
            + (errors > 0 ? $" ({errors} ошибок)" : "");

        // Обновляем списки — убираем только реально удалённые файлы
        var deletedSet = deletedPaths.Select(p => p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in DuplicateGroups.ToList())
        {
            foreach (var file in group.Files.Where(f => deletedSet.Contains(f.Path)).ToList())
                group.Files.Remove(file);

            if (group.Files.Count < 2)
                DuplicateGroups.Remove(group);
        }

        TotalGroups = DuplicateGroups.Count;
        TotalDuplicates = DuplicateGroups.Sum(g => g.Files.Count - 1);
        TotalWasted = SizeFormatter.Format(DuplicateGroups.Sum(g => g.WastedBytes));

        // Обновляем историю удалений
        LoadDeletionHistory();
    }

    // ─── Лог удалений ──────────────────────────

    [RelayCommand]
    private static void OpenRecycleBin()
    {
        try
        {
            // Обоснованное исключение (День 19, срез C): explorer.exe со
            // shell:-моникером — естественный shell-запуск (открыть корзину),
            // не исполнение команды; контракт исполнителя не применим
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "shell:RecycleBinFolder",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { /* best-effort */ Debug.WriteLine($"[DuplicatesViewModel] OpenRecycleBin error: {ex.Message}"); }
    }

    [RelayCommand]
    private void ClearDeletionHistory()
    {
        _deletionLog.ClearHistory();
        DeletionHistory.Clear();
        StatusText = "Лог удалений очищен";
    }

    /// <summary>
    /// Загружает историю удалений из файла.
    /// </summary>
    private void LoadDeletionHistory()
    {
        DeletionHistory.Clear();
        var sessions = _deletionLog.LoadHistory();
        foreach (var s in sessions.OrderByDescending(s => s.Timestamp))
            DeletionHistory.Add(new DeletionSessionViewModel(s));
    }

    /// <summary>
    /// SSS-Tier: Заменяет все дубликаты на жесткие ссылки NTFS (Hardlink Zero-Byte Deduplication)
    /// </summary>
    [RelayCommand]
    private async Task ReplaceWithHardlinksAsync()
    {
        if (DuplicateGroups.Count == 0)
        {
            StatusText = "⚠️ Нет найденных дубликатов для связывания.";
            return;
        }

        var result = MessageBox.Show(
            "Заменить найденные дубликаты на жесткие ссылки NTFS (Hardlink Zero-Byte)?\n\n" +
            "• Физическое дисковое пространство освободится (файл будет храниться на диске в 1 экземпляре).\n" +
            "• Все пути останутся полностью рабочими и совместимыми со всеми программами.",
            "Подтверждение Hardlink Deduplication",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        StatusText = "Выполняется замена дубликатов на жесткие ссылки NTFS...";

        var groups = DuplicateGroups.Select(g => new DuplicateGroup
        {
            Key = g.Key,
            Files = g.Files.Select(f => new DuplicateFile
            {
                Path = f.Path,
                Size = f.Size,
                Modified = f.Modified
            }).ToList()
        }).ToList();

        var (replaced, saved, errors) = await _engine.ReplaceDuplicatesWithHardlinksAsync(groups, new Progress<string>(s => StatusText = s));

        IsScanning = false;
        StatusText = $"Успешно заменено {replaced} дубликатов на хардлинки! Освобождено: {SizeFormatter.Format(saved)}";
        MessageBox.Show($"Создано {replaced} хардлинков.\nОсвобождено физического места: {SizeFormatter.Format(saved)}", "Hardlink Deduplication завершена", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── Drag & Drop ───────────────────────────

    /// <summary>
    /// Обрабатывает дроп файлов/папок на страницу дубликатов.
    /// Папка → добавить в «Папки для сканирования» (таб 1).
    /// Файл → переключить на таб 2 «По образцу» + заполнить путь.
    /// </summary>
    public void HandleDrop(string[] paths)
    {
        if (paths.Length == 0) return;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                // Папка → добавляем в поле сканирования дубликатов
                ScanFolders = string.IsNullOrWhiteSpace(ScanFolders)
                    ? path
                    : ScanFolders + "\n" + path;
                SelectedTabIndex = 0;
                StatusText = $"📂 Добавлена папка: {Path.GetFileName(path)}";
            }
            else if (File.Exists(path))
            {
                // Файл → помещаем в поле образца + переключаемся на таб 2
                SampleFilePath = path;
                SelectedTabIndex = 1;
                StatusText = $"📄 Файл-образец: {Path.GetFileName(path)}";
            }
        }
    }

    // ─── Helpers ────────────────────────────────

    /// <summary>
    /// Открывает диалог выбора папки.
    /// </summary>
    private static string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    /// <summary>
    /// Парсит строку с путями (разделитель — перенос строки или ;).
    /// </summary>
    private static List<string> ParsePaths(string text)
    {
        return text.Split(['\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Парсит паттерны исключений (по строкам).
    /// </summary>
    private static List<string> ParsePatterns(string text)
    {
        return text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }
}

// ────────────────────────────────────────────────
//  UI ViewModels (lightweight wrappers)
// ────────────────────────────────────────────────

/// <summary>
/// Группа дубликатов для отображения в UI.
/// </summary>
public partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(DuplicateGroup group)
    {
        Key = group.Key;
        WastedBytes = group.WastedBytes;
        WastedFormatted = SizeFormatter.Format(group.WastedBytes);
        FileSize = SizeFormatter.Format(group.Files.FirstOrDefault()?.Size ?? 0);
        Files = new ObservableCollection<DuplicateFileViewModel>(
            group.Files.Select((f, i) => new DuplicateFileViewModel(f)
            {
                IsSelected = i > 0
            }));
    }

    public string Key { get; }
    public long WastedBytes { get; }
    public string WastedFormatted { get; }
    public string FileSize { get; }
    public ObservableCollection<DuplicateFileViewModel> Files { get; }
    public int FileCount => Files.Count;
}

/// <summary>
/// Один файл-дубликат для отображения в UI.
/// </summary>
public partial class DuplicateFileViewModel : ObservableObject
{
    public DuplicateFileViewModel(DuplicateFile file)
    {
        Path = file.Path;
        Name = file.Name;
        Size = file.Size;
        SizeFormatted = SizeFormatter.Format(file.Size);
        Modified = file.Modified;
        Directory = System.IO.Path.GetDirectoryName(file.Path) ?? "";
    }

    public string Path { get; }
    public string Name { get; }
    public long Size { get; }
    public string SizeFormatted { get; }
    public DateTime Modified { get; }
    public string Directory { get; }

    [ObservableProperty] private bool _isSelected;
}

/// <summary>
/// Элемент результата сравнения папок.
/// </summary>
public sealed class CompareItemViewModel
{
    public CompareItemViewModel(string status, string path, string sizeA, string sizeB, string newer)
    {
        Status = status;
        Path = path;
        SizeA = sizeA;
        SizeB = sizeB;
        Newer = newer;
    }

    public string Status { get; }
    public string Path { get; }
    public string SizeA { get; }
    public string SizeB { get; }
    public string Newer { get; }
}

/// <summary>
/// Сессия удаления для отображения в логе.
/// </summary>
public sealed class DeletionSessionViewModel
{
    public DeletionSessionViewModel(DeletionSession session)
    {
        Timestamp = session.Timestamp;
        TimestampFormatted = session.Timestamp.ToString("dd.MM.yyyy HH:mm:ss");
        FileCount = session.Files.Count;
        TotalSize = SizeFormatter.Format(session.TotalSize);
        Files = session.Files.Select(f => f.OriginalPath).ToList();
    }

    public DateTime Timestamp { get; }
    public string TimestampFormatted { get; }
    public int FileCount { get; }
    public string TotalSize { get; }
    public List<string> Files { get; }
}
