using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.LargeFiles;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для страницы поиска больших файлов.
/// </summary>
public partial class LargeFilesViewModel : ObservableObject
{
    private readonly LargeFilesEngine _engine;
    private readonly ISafetyService _safety;
    private CancellationTokenSource? _cts;

    public LargeFilesViewModel(LargeFilesEngine engine, ISafetyService safety)
    {
        _engine = engine;
        _safety = safety;
    }

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Укажите папку и нажмите «Найти»";
    [ObservableProperty] private string _scanPath = "";
    [ObservableProperty] private string _topNText = "50";
    [ObservableProperty] private string _minSizeText = "1"; // MB
    [ObservableProperty] private string _totalSizeFormatted = "";

    public ObservableCollection<LargeFileInfo> Files { get; } = [];

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Выберите папку для поиска" };
        if (dlg.ShowDialog() == true)
            ScanPath = dlg.FolderName;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanPath) || !System.IO.Directory.Exists(ScanPath))
        {
            StatusText = "⚠️ Укажите существующую папку";
            return;
        }

        int.TryParse(TopNText, out var topN);
        if (topN <= 0) topN = 50;

        double.TryParse(MinSizeText, out var minSizeMb);
        long minBytes = (long)(Math.Max(0.1, minSizeMb) * 1024 * 1024);

        IsScanning = true;
        Files.Clear();
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var result = await _engine.ScanAsync(ScanPath, topN, minBytes, progress, _cts.Token);

            foreach (var f in result)
                Files.Add(f);

            long totalSize = result.Sum(f => f.Size);
            TotalSizeFormatted = SizeFormatter.Format(totalSize);
            StatusText = $"Найдено {result.Count} файлов, общий размер: {TotalSizeFormatted}";
        }
        catch (OperationCanceledException) { StatusText = "Поиск отменён"; }
        catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
        StatusText = "Отмена...";
    }

    /// <summary>
    /// Открывает файл в проводнике с выделением.
    /// </summary>
    [RelayCommand]
    private static void OpenInExplorer(LargeFileInfo? file)
    {
        if (file == null) return;
        try
        {
            // Обоснованное исключение (День 19, срез C): explorer.exe —
            // естественный shell-запуск (показать файл в Проводнике), не
            // исполнение команды; контракт исполнителя (CreateNoWindow,
            // редирект потоков, kill-tree) здесь не применим
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{file.Path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { /* best-effort */ Debug.WriteLine($"[LargeFilesViewModel] OpenInExplorer error: {ex.Message}"); }
    }

    /// <summary>
    /// Удаляет файл в корзину (после проверки ISafetyService).
    /// </summary>
    [RelayCommand]
    private async Task DeleteFileAsync(LargeFileInfo? file)
    {
        if (file == null) return;

        // Risk=PerformanceCache: явное удаление по клику пользователя — защищённый период
        // не применяется, но whitelist/блокировки/elevation действуют
        var validation = _safety.ValidateForDeletion(new ScannedItem
        {
            Path = file.Path,
            Size = file.Size,
            Risk = RiskCategory.PerformanceCache,
            Description = "large-file:manual"
        });

        if (!validation.CanDelete)
        {
            StatusText = $"⚠️ Заблокировано: {validation.BlockReason}";
            return;
        }

        if (validation.RequiresElevation)
        {
            StatusText = "⚠️ Файл требует прав администратора — удалите через контекстное меню проводника";
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    file.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            });

            Files.Remove(file);
            StatusText = $"🗑️ Удалено: {file.Name} ({file.SizeFormatted})";
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ Ошибка: {ex.Message}";
        }
    }
}
