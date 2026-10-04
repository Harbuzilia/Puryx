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

/// <summary>Результат восстановления системы после краша с активным бустом.</summary>
public class GameBoostRecoveryResult
{
    /// <summary>Службы, запущенные восстановлением.</summary>
    public List<string> RestoredServices { get; set; } = [];

    /// <summary>GUID плана питания, фактически возвращённый восстановлением.</summary>
    public string RestoredPowerSchemeGuid { get; set; } = string.Empty;
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

        // Запомнить текущий план питания ДО переключения — единственный шанс
        // вернуть пользователю его схему (в т.ч. после краша, через state-файл)
        CurrentState.PreviousPowerSchemeGuid = await _systemOperations.GetActivePowerSchemeGuidAsync() ?? string.Empty;

        foreach (var svcName in _candidateServices)
        {
            if (await _systemOperations.StopServiceAsync(svcName))
            {
                CurrentState.StoppedServices.Add(svcName);
            }
        }

        await _systemOperations.TrySetPowerSchemeAsync(HighPerformanceSchemeGuid);

        CurrentState.IsBoostActive = true;

        PersistState();

        progress?.Report($"🚀 Game Turbo Boost включен! (Остановлено служб: {CurrentState.StoppedServices.Count}, RAM: {CurrentState.ReclaimedMemoryFormatted})");
        return CurrentState;
    }

    public async Task<GameBoostState> DisableGameBoostAsync(IProgress<string>? progress = null)
    {
        if (!CurrentState.IsBoostActive) return CurrentState;

        progress?.Report("Восстановление стандартного режима системы...");

        // Resume stopped services
        foreach (var svcName in CurrentState.StoppedServices)
        {
            await _systemOperations.StartServiceAsync(svcName);
        }

        await RestorePowerSchemeAsync(CurrentState.PreviousPowerSchemeGuid);

        CurrentState.StoppedServices.Clear();
        CurrentState.IsBoostActive = false;

        DeleteStateFile();

        progress?.Report("Стандартный режим системы восстановлен.");
        return CurrentState;
    }

    /// <summary>
    /// Восстановление системы после краша: state-файл говорит, что буст был активен,
    /// а процесс-владелец уже не жив. Запускает остановленные службы, возвращает
    /// план питания и удаляет state-файл.
    /// Возвращает null, если восстанавливать нечего: файла нет, буст не был активен
    /// или владелец ещё жив (например, второй экземпляр приложения).
    /// </summary>
    public async Task<GameBoostRecoveryResult?> TryRecoverFromCrashAsync()
    {
        if (CurrentState.IsBoostActive) return null; // буст управляется этим процессом
        if (!_configService.Exists(StateFileName)) return null;

        var persisted = _configService.Load<GameBoostPersistedState>(StateFileName);
        if (!persisted.IsBoostActive)
        {
            DeleteStateFile();
            return null;
        }

        if (IsOwnerProcessAlive(persisted.OwnerProcessId, persisted.OwnerProcessName))
        {
            Debug.WriteLine($"[GameBoostService] Владелец буста (PID {persisted.OwnerProcessId}) жив — восстановление не требуется");
            return null;
        }

        Debug.WriteLine($"[GameBoostService] Обнаружен буст, оставленный крашем (PID {persisted.OwnerProcessId}, активирован {persisted.ActivatedAtUtc:u}) — восстанавливаю систему");

        var restoredServices = new List<string>();
        var restoredSchemeGuid = string.Empty;

        foreach (var svcName in persisted.StoppedServices)
        {
            if (await _systemOperations.StartServiceAsync(svcName))
            {
                restoredServices.Add(svcName);
            }
        }

        restoredSchemeGuid = await RestorePowerSchemeAsync(persisted.PreviousPowerSchemeGuid);

        DeleteStateFile();

        return new GameBoostRecoveryResult
        {
            RestoredServices = restoredServices,
            RestoredPowerSchemeGuid = restoredSchemeGuid
        };
    }

    /// <summary>
    /// Жив ли процесс-владелец буста. PID ненадёжен (числа переиспользуются ОС),
    /// поэтому имя процесса сверяется с записанным в state-файле.
    /// Текущий процесс не считается владельцем: к моменту вызова буст в нём не активен,
    /// значит state-файл мог остаться только от прошлого (крашнутого) запуска.
    /// </summary>
    private static bool IsOwnerProcessAlive(int ownerProcessId, string ownerProcessName)
    {
        if (ownerProcessId == Environment.ProcessId) return false;

        try
        {
            var process = Process.GetProcessById(ownerProcessId);
            var alive = string.Equals(process.ProcessName, ownerProcessName, StringComparison.OrdinalIgnoreCase);
            process.Dispose();
            return alive;
        }
        catch (ArgumentException)
        {
            return false; // PID больше не существует
        }
        catch (Exception ex)
        {
            // Не удалось подтвердить живость владельца: восстанавливаем —
            // вернуть службы важнее, чем риск двойного net start (он безвреден)
            Debug.WriteLine($"[GameBoostService] Owner process check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Вернуть план питания: сохранённый GUID, если схема ещё существует;
    /// иначе — Balanced (с логом, чтобы потеря пользовательской схемы была видна).
    /// Возвращает фактически установленный GUID.
    /// </summary>
    private async Task<string> RestorePowerSchemeAsync(string previousSchemeGuid)
    {
        if (!string.IsNullOrWhiteSpace(previousSchemeGuid))
        {
            if (await _systemOperations.PowerSchemeExistsAsync(previousSchemeGuid) &&
                await _systemOperations.TrySetPowerSchemeAsync(previousSchemeGuid))
            {
                return previousSchemeGuid;
            }

            Debug.WriteLine(
                $"[GameBoostService] Предыдущая схема питания {previousSchemeGuid} не найдена или не активировалась — возврат к Balanced");
        }
        else
        {
            Debug.WriteLine("[GameBoostService] Предыдущая схема питания неизвестна (не удалось прочитать при включении буста) — возврат к Balanced");
        }

        await _systemOperations.TrySetPowerSchemeAsync(BalancedSchemeGuid);
        return BalancedSchemeGuid;
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
