using SmartCleaner.Core.Services;
using SmartCleaner.Core.SystemOpt;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class GameBoostServiceTests : IDisposable
{
    private const string PreviousSchemeGuid = "11111111-2222-3333-4444-555555555555";

    private readonly string _configDirectory;
    private readonly FileBackedConfigService _config;
    private readonly RamOptimizerService _ramOptimizer = new();

    public GameBoostServiceTests()
    {
        _configDirectory = Path.Combine(Path.GetTempPath(), $"gameboost_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_configDirectory);
        _config = new FileBackedConfigService(_configDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDirectory, recursive: true); } catch { }
    }

    private GameBoostService CreateService(StubSystemOperations operations) =>
        new(_ramOptimizer, _config, operations);

    private string StateFilePath => Path.Combine(_configDirectory, GameBoostService.StateFileName);

    [Fact]
    public async Task EnableGameBoost_PersistsStateFile_WithStoppedServicesAndPreviousScheme()
    {
        var operations = new StubSystemOperations { ActiveSchemeGuid = PreviousSchemeGuid };
        var service = CreateService(operations);

        await service.EnableGameBoostAsync();

        Assert.True(File.Exists(StateFilePath));
        var persisted = _config.Load<GameBoostPersistedState>(GameBoostService.StateFileName);
        Assert.True(persisted.IsBoostActive);
        Assert.Equal(operations.StoppedServices, persisted.StoppedServices);
        Assert.Equal(5, persisted.StoppedServices.Count);
        Assert.Equal(PreviousSchemeGuid, persisted.PreviousPowerSchemeGuid);
        Assert.Equal(Environment.ProcessId, persisted.OwnerProcessId);
        Assert.Equal(Process.GetCurrentProcess().ProcessName, persisted.OwnerProcessName);
        Assert.True(Math.Abs((DateTime.UtcNow - persisted.ActivatedAtUtc).TotalMinutes) < 5);
    }

    [Fact]
    public async Task EnableGameBoost_ReadsActiveScheme_BeforeSwitching()
    {
        var operations = new StubSystemOperations { ActiveSchemeGuid = PreviousSchemeGuid };
        var service = CreateService(operations);

        await service.EnableGameBoostAsync();

        Assert.Equal(PreviousSchemeGuid, service.CurrentState.PreviousPowerSchemeGuid);
        var readIndex = operations.CallLog.IndexOf("getactivescheme");
        var setIndex = operations.CallLog.FindIndex(c => c.StartsWith("setscheme:", StringComparison.Ordinal));
        Assert.True(readIndex >= 0, "активная схема должна быть прочитана до переключения");
        Assert.True(setIndex > readIndex, "переключение схемы должно происходить после чтения предыдущей");
    }

    [Fact]
    public async Task DisableGameBoost_RemovesStateFile()
    {
        var service = CreateService(new StubSystemOperations());
        await service.EnableGameBoostAsync();
        Assert.True(File.Exists(StateFilePath));

        await service.DisableGameBoostAsync();

        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task DisableGameBoost_RestoresPreviousPowerScheme_WhenItStillExists()
    {
        var operations = new StubSystemOperations { ActiveSchemeGuid = PreviousSchemeGuid };
        var service = CreateService(operations);
        await service.EnableGameBoostAsync();

        await service.DisableGameBoostAsync();

        Assert.NotEmpty(operations.SetSchemeCalls);
        Assert.Equal(PreviousSchemeGuid, operations.SetSchemeCalls[^1]);
    }

    [Fact]
    public async Task DisableGameBoost_FallsBackToBalanced_WhenPreviousSchemeIsMissing()
    {
        var operations = new StubSystemOperations
        {
            ActiveSchemeGuid = PreviousSchemeGuid,
            SchemeExistsResult = _ => false
        };
        var service = CreateService(operations);
        await service.EnableGameBoostAsync();

        await service.DisableGameBoostAsync();

        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", operations.SetSchemeCalls[^1]);
    }

    [Fact]
    public async Task DisableGameBoost_FallsBackToBalanced_WhenPreviousSchemeUnknown()
    {
        var operations = new StubSystemOperations { ActiveSchemeGuid = null };
        var service = CreateService(operations);
        await service.EnableGameBoostAsync();

        await service.DisableGameBoostAsync();

        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", operations.SetSchemeCalls[^1]);
    }

    [Fact]
    public void PersistedState_RoundTripsThroughConfigService()
    {        var state = new GameBoostPersistedState
        {
            IsBoostActive = true,
            StoppedServices = ["SysMain", "WSearch"],
            PreviousPowerSchemeGuid = PreviousSchemeGuid,
            ActivatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            OwnerProcessId = 4242,
            OwnerProcessName = "Puryx"
        };

        _config.Save(GameBoostService.StateFileName, state);
        var loaded = _config.Load<GameBoostPersistedState>(GameBoostService.StateFileName);

        Assert.True(loaded.IsBoostActive);
        Assert.Equal(state.StoppedServices, loaded.StoppedServices);
        Assert.Equal(PreviousSchemeGuid, loaded.PreviousPowerSchemeGuid);
        Assert.Equal(state.ActivatedAtUtc, loaded.ActivatedAtUtc);
        Assert.Equal(4242, loaded.OwnerProcessId);
        Assert.Equal("Puryx", loaded.OwnerProcessName);
    }

    [Fact]
    public async Task RecoverAfterCrash_RestoresServicesAndScheme_WhenOwnerDead()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);
        _config.Save(GameBoostService.StateFileName, CreateActiveState(ownerProcessId: 9999999, ownerProcessName: "Puryx"));

        var result = await service.TryRecoverFromCrashAsync();

        Assert.NotNull(result);
        Assert.Equal(["SysMain", "WSearch"], operations.StartedServices);
        Assert.Equal(PreviousSchemeGuid, operations.SetSchemeCalls[^1]);
        Assert.Equal(PreviousSchemeGuid, result.RestoredPowerSchemeGuid);
        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task RecoverAfterCrash_DoesNothing_WhenOwnerIsAlive()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);
        using var other = Process.GetProcesses().First(p => p.Id != Environment.ProcessId);
        _config.Save(GameBoostService.StateFileName, CreateActiveState(other.Id, other.ProcessName));

        var result = await service.TryRecoverFromCrashAsync();

        Assert.Null(result);
        Assert.Empty(operations.StartedServices);
        Assert.Empty(operations.SetSchemeCalls);
        Assert.True(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task RecoverAfterCrash_Restores_WhenOwnerPidReusedByDifferentProcess()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);
        // PID жив, но занят другим процессом (имя не совпадает) — владелец мёртв
        using var other = Process.GetProcesses().First(p => p.Id != Environment.ProcessId);
        _config.Save(GameBoostService.StateFileName, CreateActiveState(other.Id, "Puryx"));

        var result = await service.TryRecoverFromCrashAsync();

        Assert.NotNull(result);
        Assert.Equal(["SysMain", "WSearch"], operations.StartedServices);
        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task RecoverAfterCrash_Restores_WhenOwnerIsCurrentProcess()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);
        // PID переиспользован самим приложением: буст в этом процессе не активен,
        // значит state-файл остался от прошлого (крашнутого) запуска
        _config.Save(GameBoostService.StateFileName,
            CreateActiveState(Environment.ProcessId, Process.GetCurrentProcess().ProcessName));

        var result = await service.TryRecoverFromCrashAsync();

        Assert.NotNull(result);
        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task RecoverAfterCrash_IgnoresInactiveStateFile()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);
        var state = CreateActiveState(9999999, "Puryx");
        state.IsBoostActive = false;
        _config.Save(GameBoostService.StateFileName, state);

        var result = await service.TryRecoverFromCrashAsync();

        Assert.Null(result);
        Assert.Empty(operations.CallLog);
        Assert.False(File.Exists(StateFilePath));
    }

    [Fact]
    public async Task RecoverAfterCrash_DoesNothing_WithoutStateFile()
    {
        var operations = new StubSystemOperations();
        var service = CreateService(operations);

        var result = await service.TryRecoverFromCrashAsync();

        Assert.Null(result);
        Assert.Empty(operations.CallLog);
    }

    private static GameBoostPersistedState CreateActiveState(int ownerProcessId, string ownerProcessName) => new()
    {
        IsBoostActive = true,
        StoppedServices = ["SysMain", "WSearch"],
        PreviousPowerSchemeGuid = PreviousSchemeGuid,
        ActivatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        OwnerProcessId = ownerProcessId,
        OwnerProcessName = ownerProcessName
    };

    private sealed class FileBackedConfigService : IConfigService
    {
        private readonly string _configDirectory;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public FileBackedConfigService(string configDirectory)
        {
            _configDirectory = configDirectory;
        }

        public string ConfigDirectory => _configDirectory;
        public ConfigMode Mode => ConfigMode.Portable;
        public string LogDirectory => _configDirectory;
        public string UserRulesDirectory => _configDirectory;

        public T Load<T>(string filename) where T : new()
        {
            var path = Path.Combine(_configDirectory, filename);
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T();
        }

        public void Save<T>(string filename, T data) =>
            File.WriteAllText(Path.Combine(_configDirectory, filename), JsonSerializer.Serialize(data));

        public bool Exists(string filename) => File.Exists(Path.Combine(_configDirectory, filename));
    }

    private sealed class StubSystemOperations : IGameBoostSystemOperations
    {
        public string? ActiveSchemeGuid { get; set; } = PreviousSchemeGuid;
        public Func<string, bool> StopServiceResult { get; set; } = _ => true;
        public Func<string, bool> StartServiceResult { get; set; } = _ => true;
        public Func<string, bool> SchemeExistsResult { get; set; } = _ => true;
        public Func<string, bool> SetSchemeResult { get; set; } = _ => true;

        public List<string> StoppedServices { get; } = [];
        public List<string> StartedServices { get; } = [];
        public List<string> SetSchemeCalls { get; } = [];
        public List<string> CallLog { get; } = [];

        public Task<bool> StopServiceAsync(string serviceName, CancellationToken ct = default)
        {
            CallLog.Add($"stopservice:{serviceName}");
            StoppedServices.Add(serviceName);
            return Task.FromResult(StopServiceResult(serviceName));
        }

        public Task<bool> StartServiceAsync(string serviceName, CancellationToken ct = default)
        {
            CallLog.Add($"startservice:{serviceName}");
            StartedServices.Add(serviceName);
            return Task.FromResult(StartServiceResult(serviceName));
        }

        public Task<string?> GetActivePowerSchemeGuidAsync(CancellationToken ct = default)
        {
            CallLog.Add("getactivescheme");
            return Task.FromResult(ActiveSchemeGuid);
        }

        public Task<bool> TrySetPowerSchemeAsync(string schemeGuid, CancellationToken ct = default)
        {
            CallLog.Add($"setscheme:{schemeGuid}");
            SetSchemeCalls.Add(schemeGuid);
            return Task.FromResult(SetSchemeResult(schemeGuid));
        }

        public Task<bool> PowerSchemeExistsAsync(string schemeGuid, CancellationToken ct = default)
        {
            CallLog.Add($"schemeexists:{schemeGuid}");
            return Task.FromResult(SchemeExistsResult(schemeGuid));
        }
    }
}
