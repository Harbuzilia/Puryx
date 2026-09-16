using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Compression;
using SmartCleaner.Core.Helpers;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class CompactViewModel : ObservableObject
{
    private readonly CompactEngine _engine;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Нажмите «Найти объекты для сжатия» для анализа установленных игр и программ";

    [ObservableProperty]
    private int _totalTargetsCount;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    [ObservableProperty]
    private string _estimatedSavingsFormatted = "0 B";

    public ObservableCollection<CompactTargetItem> Targets { get; } = new();

    public CompactViewModel(CompactEngine engine)
    {
        _engine = engine;
    }

    [RelayCommand]
    public async Task DiscoverAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Поиск установленных игр и тяжелых папок разработки...";

        try
        {
            var list = await _engine.DiscoverCompressibleTargetsAsync();
            Targets.Clear();

            long totalBytes = 0;
            foreach (var item in list)
            {
                Targets.Add(item);
                totalBytes += item.OriginalSizeBytes;
            }

            TotalTargetsCount = Targets.Count;
            TotalSizeFormatted = SizeFormatter.Format(totalBytes);
            var estimatedSavings = (long)(totalBytes * 0.35); // ~35% average savings
            EstimatedSavingsFormatted = SizeFormatter.Format(estimatedSavings);

            StatusText = $"Найдено {TotalTargetsCount} объектов ({TotalSizeFormatted}). Потенциальная экономия: ~{EstimatedSavingsFormatted}";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CompressTargetAsync(CompactTargetItem? target)
    {
        if (target == null || IsBusy) return;

        var result = MessageBox.Show(
            $"Выполнить прозрачное сжатие NTFS (алгоритм {target.RecommendedAlgorithm}) для:\n\n{target.Name}\nПуть: {target.Path}\nРазмер: {target.OriginalSizeFormatted}\n\nФайлы останутся полностью доступными, распаковка происходит на лету без потери производительности.",
            "Подтверждение сжатия CompactOS LZX",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var (success, msg, saved) = await _engine.CompressDirectoryAsync(target.Path, target.RecommendedAlgorithm, progress, _cts.Token);
            if (success)
            {
                target.IsCompressed = true;
                target.Status = $"Сжато ({SizeFormatter.Format(saved)} сэкономлено)";
                StatusText = msg;
                MessageBox.Show(msg, "Сжатие завершено", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = $"Ошибка: {msg}";
                MessageBox.Show(msg, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Исключение: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public async Task DecompressTargetAsync(CompactTargetItem? target)
    {
        if (target == null || IsBusy) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg => StatusText = msg);

        try
        {
            var (success, msg) = await _engine.DecompressDirectoryAsync(target.Path, progress, _cts.Token);
            if (success)
            {
                target.IsCompressed = false;
                target.Status = "Не сжато";
                StatusText = msg;
                MessageBox.Show(msg, "Распаковка завершена", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = $"Ошибка: {msg}";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Исключение: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
