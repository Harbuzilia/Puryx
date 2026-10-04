using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.ServicesOpt;
using Xunit;
using Xunit.Abstractions;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 4 — H2 (ROADMAP): ложный успех при настройке служб.
/// sc.exe для несуществующей службы завершается кодом 1060 — SetServiceStartupAsync
/// обязан возвращать false по фактическому ExitCode, а не «true после WaitForExit».
/// День 17 — срез A: контрактные тесты команд через стаб ICommandExecutor —
/// фиксируют полное имя утилиты (SystemToolLocator), аргументы и таймаут.
/// Полный мутационный сюит — День 22 (P3).
/// </summary>
public class WindowsServicesOptimizerTests
{
    private readonly ITestOutputHelper _output;

    public WindowsServicesOptimizerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SetServiceStartupAsync_NonExistentService_ReturnsFalse()
    {
        var optimizer = new WindowsServicesOptimizer();

        // sc config на несуществующей службе печатает «FAILED 1060:
        // The specified service does not exist» и возвращает код 1060 —
        // успех невозможен, вызов не меняет систему
        var result = await optimizer.SetServiceStartupAsync(
            "PuryxUnitTestNoSuchService", ServiceStartupType.Disabled);

        Assert.False(result);
    }

    [Fact]
    public async Task SetServiceStartupAsync_Disabled_IssuesScConfigAndNetStopThroughExecutor()
    {
        var executor = new RecordingCommandExecutor();
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Disabled);

        Assert.True(result);
        Assert.Equal(2, executor.Requests.Count);

        var config = executor.Requests[0];
        Assert.Equal(SystemToolLocator.GetScPath(), config.FileName);
        Assert.Equal(new[] { "config", "PuryxStubService", "start=", "disabled" }, config.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(3000), config.Timeout);

        var stop = executor.Requests[1];
        Assert.Equal(SystemToolLocator.GetNetPath(), stop.FileName);
        Assert.Equal(new[] { "stop", "PuryxStubService", "/y" }, stop.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(10000), stop.Timeout);

        foreach (var request in executor.Requests)
        {
            _output.WriteLine(RecordingCommandExecutor.Describe(request));
        }
    }

    [Fact]
    public async Task SetServiceStartupAsync_ScConfigFails_ReturnsFalseWithoutNetStop()
    {
        // ExitCode-гвард: код 1060 — не успех; net stop после отказа конфигурации не вызывается
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1060, "FAILED 1060: The specified service does not exist");
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Disabled);

        Assert.False(result);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task SetServiceStartupAsync_NetStopFailsOnStoppedService_IsBenign()
    {
        // net stop незапущенной службы ≠ сбой (день 5): цель «остановлена» достигнута,
        // если sc query подтверждает, что служба не работает
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(2, "The service is not started.");
        executor.EnqueueSuccess("STATE            : 1  STOPPED");
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Disabled);

        Assert.True(result);
        Assert.Equal(3, executor.Requests.Count);
        Assert.Equal(new[] { "query", "PuryxStubService" }, executor.Requests[2].Arguments);
    }

    [Fact]
    public async Task SetServiceStartupAsync_NetStopFails_ReturnsFalseWhenStillRunning()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(2, "The service is not started.");
        executor.EnqueueSuccess("STATE            : 4  RUNNING");
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Disabled);

        Assert.False(result);
    }

    [Fact]
    public async Task ScanServicesAsync_QueriesEachKnownServiceThroughExecutor()
    {
        var executor = new RecordingCommandExecutor();
        var optimizer = new WindowsServicesOptimizer(executor);

        var services = await optimizer.ScanServicesAsync();

        Assert.Equal(optimizer.GetKnownServices().Count, services.Count);
        Assert.Equal(services.Count, executor.Requests.Count);
        Assert.All(executor.Requests, r => Assert.Equal(SystemToolLocator.GetScPath(), r.FileName));
        Assert.Contains(executor.Requests, r => r.Arguments.SequenceEqual(new[] { "query", "DiagTrack" }));
    }
}
