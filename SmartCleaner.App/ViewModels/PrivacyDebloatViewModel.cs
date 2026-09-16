using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.App.Services;
using SmartCleaner.Core.Privacy;
using System.Collections.ObjectModel;

namespace SmartCleaner.App.ViewModels;

public partial class PrivacyDebloatViewModel : ObservableObject
{
    private readonly PrivacyDebloatService _privacyService;
    private readonly AudioFeedbackService _audioService;
    private List<PrivacyTweakItem> _allTweaks = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Готов к проверке параметров приватности и телеметрии";

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
        StatusText = "Сканирование параметров телеметрии и реестра...";

        try
        {
            await ReloadTweaksAsync();
            StatusText = $"Сканирование завершено: отключено {AppliedCount} из {TotalCount} параметров сбора данных.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка сканирования: {ex.Message}";
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
        StatusText = targetState ? $"Отключение {tweak.Title}..." : $"Восстановление {tweak.Title}...";

        try
        {
            bool success = targetState
                ? await _privacyService.ApplyTweakAsync(tweak.Id)
                : await _privacyService.RevertTweakAsync(tweak.Id);

            if (success)
            {
                tweak.IsApplied = targetState;
                _audioService.PlayScanComplete();
                StatusText = targetState ? $"Отключено: {tweak.Title}" : $"Восстановлено: {tweak.Title}";
            }
            else
            {
                StatusText = $"Не удалось изменить статус {tweak.Title}. Требуются права администратора.";
            }

            AppliedCount = _allTweaks.Count(t => t.IsApplied);
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
    public async Task ApplyAllRecommendedAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Отключение всех рекомендуемых параметров телеметрии и рекламы...";

        try
        {
            int applied = await _privacyService.ApplyAllRecommendedAsync();
            _audioService.PlayCleanComplete();
            await ReloadTweaksAsync();
            StatusText = $"Успешно отключено {applied} рекомендуемых параметров сбора данных и рекламы.";
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
    public async Task RestoreDefaultsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Восстановление стандартных параметров Windows по умолчанию...";

        try
        {
            int restored = await _privacyService.RestoreAllDefaultsAsync();
            _audioService.PlayBoostActivated();
            await ReloadTweaksAsync();
            StatusText = $"Восстановлено {restored} параметров в исходное состояние Windows.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка восстановления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
