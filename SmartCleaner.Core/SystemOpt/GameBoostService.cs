using System.Diagnostics;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.SystemOpt;

public class GameBoostState
{
    public bool IsBoostActive { get; set; }
    public List<string> StoppedServices { get; set; } = [];
    public string PreviousPowerSchemeGuid { get; set; } = string.Empty;
    public string ReclaimedMemoryFormatted { get; set; } = "0 MB";
}

/// <summary>
/// Состояние буста, переживаемое перезапуск приложения: сохраняется в файл
/// <see cref="GameBoostService.StateFileName"/> через IConfigService (portable/installed режимы).
/// Позволяет восстановить остановленные службы и план питания после краша.
/// </summary>
public class GameBoostPersistedState
{
    public bool IsBoostActive { get; set; }
    public List<string> StoppedServices { get; set; } = [];
    public string PreviousPowerSchemeGuid { get; set; } = string.Empty;
    public DateTime ActivatedAtUtc { get; set; }
    public int OwnerProcessId { get; set; }
    public string OwnerProcessName { get; set; } = string.Empty;
}

public class GameBoostService
{
    private const string HighPerformanceSchemeGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedSchemeGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    /// <summary>Имя файла состояния буста в ConfigDirectory (IConfigService).</summary>
    public const string StateFileName = "gameboost_state.json";

    private readonly RamOptimizerService _ramOptimizer;
    private readonly IConfigService _configService;
    private readonly IGameBoostSystemOperations _systemOperations;
    private readonly List<string> _candidateServices =
    [
        "SysMain",     // Superfetch / Prefetch
        "wuauserv",    // Windows Update
        "DiagTrack",   // Telemetry
        "dmwappushservice", // WAP Push Message Routing
        "WSearch"      // Windows Search Indexer
    ];

    public GameBoostState CurrentState { get; } = new();

    public GameBoostService(
        RamOptimizerService ramOptimizer,
        IConfigService configService,
        IGameBoostSystemOperations? systemOperations = null)
    {
        _ramOptimizer = ramOptimizer;
        _configService = configService;
        _systemOperations = systemOperations ?? new StandardGameBoostSystemOperations();
    }

    public async Task<GameBoostState> EnableGameBoostAsync(IProgress<string>? progress = null)
    {
        if (CurrentState.IsBoostActive) return CurrentState;

        progress?.Report("🎮 Активация Game Turbo Boost...");

        // 1. Optimize RAM
        progress?.Report("Очистка и сжатие оперативной памяти...");
        var ramResult = await _ramOptimizer.OptimizeMemoryAsync();
        CurrentState.ReclaimedMemoryFormatted = ramResult.ReclaimedFormatted;

        // 2. Pause non-essential background services
        progress?.Report("Приостановка фоновых служб (SysMain, Telemetry, Windows Update)...");
        CurrentState.StoppedServices.Clear();

        await Task.Run(() =>
        {
            // Запомнить текущий план питания ДО переключения — единственный шанс
            // вернуть пользователю его схему (в т.ч. после краша, через state-файл)
            CurrentState.PreviousPowerSchemeGuid = _systemOperations.GetActivePowerSchemeGuid() ?? string.Empty;

            foreach (var svcName in _candidateServices)
            {
                if (_systemOperations.StopService(svcName))
                {
                    CurrentState.StoppedServices.Add(svcName);
                }
            }

            _systemOperations.TrySetPowerScheme(HighPerformanceSchemeGuid);
        });

        CurrentState.IsBoostActive = true;

        PersistState();

        progress?.Report($"🚀 Game Turbo Boost включен! (Остановлено служб: {CurrentState.StoppedServices.Count}, RAM: {CurrentState.ReclaimedMemoryFormatted})");
        return CurrentState;
    }

    public async Task<GameBoostState> DisableGameBoostAsync(IProgress<string>? progress = null)
    {
        if (!CurrentState.IsBoostActive) return CurrentState;

        progress?.Report("Восстановление стандартного режима системы...");

        await Task.Run(() =>
        {
            // Resume stopped services
            foreach (var svcName in CurrentState.StoppedServices)
            {
                _systemOperations.StartService(svcName);
            }

            _systemOperations.TrySetPowerScheme(BalancedSchemeGuid);
        });

        CurrentState.StoppedServices.Clear();
        CurrentState.IsBoostActive = false;

        DeleteStateFile();

        progress?.Report("Стандартный режим системы восстановлен.");
        return CurrentState;
    }

    /// <summary>
    /// Сохранить состояние буста в state-файл: остановленные службы, предыдущий план питания,
    /// время активации и PID-владельца — для восстановления после краша.
    /// </summary>
    private void PersistState()
    {
        try
        {
            _configService.Save(StateFileName, new GameBoostPersistedState
            {
                IsBoostActive = CurrentState.IsBoostActive,
                StoppedServices = [.. CurrentState.StoppedServices],
                PreviousPowerSchemeGuid = CurrentState.PreviousPowerSchemeGuid,
                ActivatedAtUtc = DateTime.UtcNow,
                OwnerProcessId = Environment.ProcessId,
                OwnerProcessName = Process.GetCurrentProcess().ProcessName
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoostService] Persist state failed: {ex.Message}");
        }
    }

    /// <summary>Удалить state-файл буста (буст отключён — восстанавливать нечего).</summary>
    private void DeleteStateFile()
    {
        try
        {
            var path = Path.Combine(_configService.ConfigDirectory, StateFileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoostService] Delete state file failed: {ex.Message}");
        }
    }
}
