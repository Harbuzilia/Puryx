using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.App.Services;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Privacy;
using System.Collections.ObjectModel;

namespace SmartCleaner.App.ViewModels;

public partial class PrivacyDebloatViewModel : ObservableObject
{
    private readonly PrivacyDebloatService _privacyService;
    private readonly AudioFeedbackService _audioService;
    private List<PrivacyTweakItem> _allTweaks = [];

    /// <summary>CancellationSource минутного профиля (День 21, L3) — по образцу CompactViewModel.</summary>
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Localizer.GetString("PrivacyDebloat_Status_Ready");

    [ObservableProperty]
    private int _appliedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _selectedCategoryIndex;

    public ObservableCollection<PrivacyTweakItem> FilteredTweaks { get; } = new();

    public PrivacyDebloatViewModel(PrivacyDebloatService privacyService, AudioFeedbackService audioService)
    {
        _privacyService = privacyService;
        _audioService = audioService;
        _ = ScanAsync();
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = Localizer.GetString("PrivacyDebloat_Status_Scanning");

        try
        {
            await ReloadTweaksAsync();
            StatusText = Localizer.GetString("PrivacyDebloat_Status_ScanDone", AppliedCount, TotalCount);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("PrivacyDebloat_Status_ScanError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Перезагружает список твиков без проверки IsBusy — вызывается из команд,
    /// которые уже держат IsBusy=true (guard в ScanAsync сделал бы перезагрузку no-op).
    /// </summary>
    private async Task ReloadTweaksAsync()
    {
        _allTweaks = await _privacyService.ScanStatusesAsync();
        TotalCount = _allTweaks.Count;
        AppliedCount = _allTweaks.Count(t => t.IsApplied);
        ApplyFilter();
    }

    [RelayCommand]
    public async Task ToggleTweakAsync(PrivacyTweakItem tweak)
    {
        if (tweak == null || IsBusy) return;

        IsBusy = true;
        bool targetState = !tweak.IsApplied;
        StatusText = targetState ? Localizer.GetString("PrivacyDebloat_Status_Disabling", tweak.Title) : Localizer.GetString("PrivacyDebloat_Status_Reverting", tweak.Title);

        try
        {
            bool success = targetState
                ? await _privacyService.ApplyTweakAsync(tweak.Id)
                : await _privacyService.RevertTweakAsync(tweak.Id);

            if (success)
            {
                tweak.IsApplied = targetState;
                _audioService.PlayScanComplete();
                StatusText = targetState ? Localizer.GetString("PrivacyDebloat_Status_Disabled", tweak.Title) : Localizer.GetString("PrivacyDebloat_Status_Reverted", tweak.Title);
            }
            else
            {
                StatusText = Localizer.GetString("PrivacyDebloat_Status_ChangeFailedAdmin", tweak.Title);
            }

            AppliedCount = _allTweaks.Count(t => t.IsApplied);
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
    public async Task ApplyAllRecommendedAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = Localizer.GetString("PrivacyDebloat_Status_ApplyingRecommended");

        try
        {
            int applied = await _privacyService.ApplyAllRecommendedAsync(_cts.Token);
            _audioService.PlayCleanComplete();
            await ReloadTweaksAsync();
            StatusText = Localizer.GetString("PrivacyDebloat_Status_RecommendedDone", applied);
        }
        catch (OperationCanceledException)
        {
            // Отмена длинного профиля: часть твиков могла примениться до нажатия —
            // перечитываем фактическое состояние, а не показываем устаревший список
            await ReloadTweaksAsync();
            StatusText = Localizer.GetString("PrivacyDebloat_Status_Cancelled");
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
    public async Task RestoreDefaultsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = Localizer.GetString("PrivacyDebloat_Status_RestoringDefaults");

        try
        {
            int restored = await _privacyService.RestoreAllDefaultsAsync(_cts.Token);
            _audioService.PlayBoostActivated();
            await ReloadTweaksAsync();
            StatusText = Localizer.GetString("PrivacyDebloat_Status_RestoredCount", restored);
        }
        catch (OperationCanceledException)
        {
            await ReloadTweaksAsync();
            StatusText = Localizer.GetString("PrivacyDebloat_Status_Cancelled");
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_RestoreError", ex.Message);
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Отменяет выполняемый профиль (День 21, L3) — паттерн CompactViewModel (2.7.2):
    /// токен доходит до исполнителя команд, между итерациями профиль прерывается.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredTweaks.Clear();
        IEnumerable<PrivacyTweakItem> query = _allTweaks;

        if (SelectedCategoryIndex == 1)
            query = query.Where(t => t.Category == PrivacyCategory.Telemetry);
        else if (SelectedCategoryIndex == 2)
            query = query.Where(t => t.Category == PrivacyCategory.Advertising);
        else if (SelectedCategoryIndex == 3)
            query = query.Where(t => t.Category == PrivacyCategory.FeedbackAndErrors);
        else if (SelectedCategoryIndex == 4)
            query = query.Where(t => t.Category == PrivacyCategory.TrackingAndSensors);

        foreach (var item in query)
        {
            FilteredTweaks.Add(item);
        }
    }
}
