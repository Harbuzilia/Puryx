using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Compression;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
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
    private string _statusText = Localizer.GetString("Compact_Status_PressAnalyze");

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
        StatusText = Localizer.GetString("Compact_Status_Searching");

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

            StatusText = Localizer.GetString("Compact_Status_Found", TotalTargetsCount, TotalSizeFormatted, EstimatedSavingsFormatted);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Compact_Status_SearchError", ex.Message);
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
            Localizer.GetString("Compact_CompressConfirmBody", target.RecommendedAlgorithm, target.Name, target.Path, target.OriginalSizeFormatted),
            Localizer.GetString("Compact_CompressConfirmCaption"),
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
                target.Status = saved > 0
                    ? Localizer.GetString("Compact_Status_CompressedSaved", SizeFormatter.Format(saved))
                    : Localizer.GetString("Compact_Status_CompressedNoData");
                StatusText = msg;
                MessageBox.Show(msg, Localizer.GetString("Compact_Caption_CompressDone"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = Localizer.GetString("Common_Status_Error", msg);
                MessageBox.Show(msg, Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
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
                target.Status = Localizer.GetString("Compact_Status_NotCompressed");
                StatusText = msg;
                MessageBox.Show(msg, Localizer.GetString("Compact_Caption_DecompressDone"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StatusText = Localizer.GetString("Common_Status_Error", msg);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
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
