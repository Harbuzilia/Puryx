using System.Diagnostics;

namespace SmartCleaner.Core.SystemOpt;

public class GameBoostState
{
    public bool IsBoostActive { get; set; }
    public List<string> StoppedServices { get; set; } = [];
    public string PreviousPowerSchemeGuid { get; set; } = string.Empty;
    public string ReclaimedMemoryFormatted { get; set; } = "0 MB";
}

public class GameBoostService
{
    private readonly RamOptimizerService _ramOptimizer;
    private readonly List<string> _candidateServices =
    [
        "SysMain",     // Superfetch / Prefetch
        "wuauserv",    // Windows Update
        "DiagTrack",   // Telemetry
        "dmwappushservice", // WAP Push Message Routing
        "WSearch"      // Windows Search Indexer
    ];

    public GameBoostState CurrentState { get; } = new();

    public GameBoostService(RamOptimizerService ramOptimizer)
    {
        _ramOptimizer = ramOptimizer;
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
            foreach (var svcName in _candidateServices)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = $"stop {svcName}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(1500);
                        if (proc.ExitCode == 0)
                        {
                            CurrentState.StoppedServices.Add(svcName);
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[GameBoostService] Service stop error: {ex.Message}"); }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "-setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch (Exception ex) { Debug.WriteLine($"[GameBoostService] Powercfg enable error: {ex.Message}"); }
        });

        CurrentState.IsBoostActive = true;
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
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "net.exe",
                        Arguments = $"start {svcName}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(1500);
                }
                catch (Exception ex) { Debug.WriteLine($"[GameBoostService] Service start error: {ex.Message}"); }
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(2000);
            }
            catch (Exception ex) { Debug.WriteLine($"[GameBoostService] Powercfg disable error: {ex.Message}"); }
        });

        CurrentState.StoppedServices.Clear();
        CurrentState.IsBoostActive = false;
        progress?.Report("Стандартный режим системы восстановлен.");
        return CurrentState;
    }
}
