using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.App.Services;
using SmartCleaner.Core.ServicesOpt;
using System.Collections.ObjectModel;

namespace SmartCleaner.App.ViewModels;

public partial class ServicesOptimizerViewModel : ObservableObject
{
    private readonly WindowsServicesOptimizer _servicesOptimizer;
    private readonly AudioFeedbackService _audioService;
    private List<WindowsServiceItem> _allServices = [];

    /// <summary>CancellationSource минутного профиля (День 21, L3) — по образцу CompactViewModel.</summary>
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Готов к сканированию и оптимизации фоновых служб Windows";

    [ObservableProperty]
    private int _disabledCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _selectedRiskFilterIndex;

    public ObservableCollection<WindowsServiceItem> FilteredServices { get; } = new();

    public ServicesOptimizerViewModel(WindowsServicesOptimizer servicesOptimizer, AudioFeedbackService audioService)
    {
        _servicesOptimizer = servicesOptimizer;
        _audioService = audioService;
        _ = ScanAsync();
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Опрос состояния и типов запуска системных служб...";

        try
        {
            await ReloadServicesAsync();
            StatusText = $"Сканирование завершено: {DisabledCount} из {TotalCount} служб отключено.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка опроса служб: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Перезагружает список служб без проверки IsBusy — вызывается из команд,
    /// которые уже держат IsBusy=true (guard в ScanAsync сделал бы перезагрузку no-op).
    /// </summary>
    private async Task ReloadServicesAsync()
    {
        _allServices = await _servicesOptimizer.ScanServicesAsync();
        TotalCount = _allServices.Count;
        DisabledCount = _allServices.Count(s => s.IsDisabled);
        ApplyFilter();
    }

    [RelayCommand]
    public async Task ToggleServiceAsync(WindowsServiceItem service)
    {
        if (service == null || IsBusy) return;

        IsBusy = true;
        var targetType = service.IsDisabled ? ServiceStartupType.Manual : ServiceStartupType.Disabled;
        StatusText = $"Изменение службы {service.DisplayName}...";

        try
        {
            bool success = await _servicesOptimizer.SetServiceStartupAsync(service.ServiceName, targetType);
            if (success)
            {
                service.StartupType = targetType;
                service.IsRunning = targetType != ServiceStartupType.Disabled && service.IsRunning;
                _audioService.PlayScanComplete();
                StatusText = $"Служба {service.DisplayName} переведена в режим {service.StartupTypeFormatted}.";
            }
            else
            {
                StatusText = $"Не удалось изменить службу {service.DisplayName}. Требуются права администратора.";
            }

            DisabledCount = _allServices.Count(s => s.IsDisabled);
            ApplyFilter();
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
    public async Task ApplyGamingProfileAsync()
    {
        await ApplyProfileInternalAsync(ServiceProfileType.Gaming, "🎮 Применение игрового профиля (максимум FPS и минимум задержек)...");
    }

    [RelayCommand]
    public async Task ApplyBalancedProfileAsync()
    {
        await ApplyProfileInternalAsync(ServiceProfileType.Balanced, "⚡ Применение сбалансированного профиля (безопасное отключение балласта)...");
    }

    [RelayCommand]
    public async Task ApplyWorkstationProfileAsync()
    {
        await ApplyProfileInternalAsync(ServiceProfileType.Workstation, "💼 Применение офисного профиля (сохранение сети и принтеров)...");
    }

    [RelayCommand]
    public async Task RestoreDefaultsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = "Восстановление служб по умолчанию из резервной копии...";

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            int restored = await _servicesOptimizer.RestoreDefaultServicesAsync(progress, _cts.Token);
            _audioService.PlayBoostActivated();
            await ReloadServicesAsync();
            StatusText = $"Восстановлено {restored} служб в стандартное состояние Windows.";
        }
        catch (OperationCanceledException)
        {
            // Отмена длинного восстановления: часть служб могла быть настроена —
            // перечитываем фактическое состояние
            await ReloadServicesAsync();
            StatusText = "Операция отменена. Показано фактическое состояние служб.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка восстановления: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ApplyProfileInternalAsync(ServiceProfileType profileType, string startMsg)
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = startMsg;

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            int modified = await _servicesOptimizer.ApplyProfileAsync(profileType, progress, _cts.Token);
            _audioService.PlayCleanComplete();
            await ReloadServicesAsync();
            StatusText = $"Профиль применен. Настроено {modified} служб.";
        }
        catch (OperationCanceledException)
        {
            await ReloadServicesAsync();
            StatusText = "Профиль отменен. Показано фактическое состояние служб.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка применения профиля: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Отменяет выполняемый профиль служб (День 21, L3) — паттерн CompactViewModel (2.7.2):
    /// токен доходит до исполнителя команд, между итерациями профиль прерывается.
    /// </summary>
    [RelayCommand]
    public void Cancel()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    partial void OnSelectedRiskFilterIndexChanged(int value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredServices.Clear();
        IEnumerable<WindowsServiceItem> query = _allServices;

        if (SelectedRiskFilterIndex == 1)
            query = query.Where(s => s.RiskLevel == ServiceRiskLevel.SafeToDisable);
        else if (SelectedRiskFilterIndex == 2)
            query = query.Where(s => s.RiskLevel == ServiceRiskLevel.Moderate);

        foreach (var item in query)
        {
            FilteredServices.Add(item);
        }
    }
}
