using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.App.Services;
using SmartCleaner.Core.Network;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class NetworkOptimizerViewModel : ObservableObject
{
    private readonly NetworkOptimizerService _networkService;
    private readonly AudioFeedbackService _audioService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Готов к оптимизации сетевых задержек и DNS";

    [ObservableProperty]
    private bool _isTcpNoDelayEnabled;

    public ObservableCollection<DnsPreset> Presets { get; } = new();

    public NetworkOptimizerViewModel(NetworkOptimizerService networkService, AudioFeedbackService audioService)
    {
        _networkService = networkService;
        _audioService = audioService;

        foreach (var p in _networkService.Presets)
        {
            Presets.Add(p);
        }

        _ = BenchmarkDnsAsync();
    }

    [RelayCommand]
    public async Task BenchmarkDnsAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Замер сетевых задержек DNS-серверов (Ping)...";

        try
        {
            await _networkService.BenchmarkDnsPresetsAsync();
            Presets.Clear();
            foreach (var p in _networkService.Presets)
            {
                Presets.Add(p);
            }
            StatusText = "Замер задержек DNS завершен.";
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
    public async Task FlushDnsAndWinsockAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Очистка DNS кэша и сброс сокетов Winsock...";

        try
        {
            var (ok, msg) = await _networkService.FlushDnsAndResetWinsockAsync(new Progress<string>(s => StatusText = s));
            StatusText = msg;
            if (ok) _audioService.PlayCleanComplete();
            MessageBox.Show(msg, "Network Optimizer", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
    public async Task ApplyDnsPresetAsync(DnsPreset? preset)
    {
        if (preset == null || IsBusy) return;

        IsBusy = true;
        StatusText = $"Установка DNS {preset.Name}...";

        try
        {
            var (ok, msg) = await _networkService.ApplyDnsAsync(preset.Primary, preset.Secondary);
            StatusText = msg;
            if (ok) _audioService.PlayBoostActivated();
            MessageBox.Show(msg, "DNS Switcher", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
    public async Task ResetToDhcpAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = "Сброс DNS на автоматический (DHCP)...";

        try
        {
            var (ok, msg) = await _networkService.ResetDnsToDhcpAsync();
            StatusText = msg;
            MessageBox.Show(msg, "DNS Switcher", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
    public async Task ToggleTcpNoDelayAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var (ok, msg) = await _networkService.SetTcpNoDelayGamingTweakAsync(IsTcpNoDelayEnabled);
            StatusText = msg;
            MessageBox.Show(msg, "TCP Gaming Optimizer", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
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
}
