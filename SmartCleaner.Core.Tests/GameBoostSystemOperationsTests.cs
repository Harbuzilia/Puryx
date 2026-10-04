using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.SystemOpt;
using Xunit;
using Xunit.Abstractions;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 17 — срез A (ROADMAP): контрактные тесты исполнителя команд для
/// системных операций Game Boost (net.exe / powercfg). Стаб фиксирует
/// полное имя утилиты (SystemToolLocator), аргументы и таймаут.
/// </summary>
public class GameBoostSystemOperationsTests
{
    private readonly ITestOutputHelper _output;

    public GameBoostSystemOperationsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task StopService_IssuesNetStopWithFullToolPathAndTimeout()
    {
        var executor = new RecordingCommandExecutor();
        var operations = new StandardGameBoostSystemOperations(executor);

        Assert.True(await operations.StopServiceAsync("SysMain"));

        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetNetPath(), request.FileName);
        Assert.Equal(new[] { "stop", "SysMain" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), request.Timeout);

        _output.WriteLine(RecordingCommandExecutor.Describe(request));
    }

    [Fact]
    public async Task StartService_FailsByExitCode_ReturnsFalse()
    {
        // ExitCode-гвард: код возврата 2 — служба не запущена, это отказ
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(2, "The service cannot be started");
        var operations = new StandardGameBoostSystemOperations(executor);

        Assert.False(await operations.StartServiceAsync("SysMain"));

        Assert.Equal(new[] { "start", "SysMain" }, executor.Requests[0].Arguments);
    }

    [Fact]
    public async Task GetActivePowerSchemeGuid_ParsesGuidFromPowercfgOutput()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)");
        var operations = new StandardGameBoostSystemOperations(executor);

        var guid = await operations.GetActivePowerSchemeGuidAsync();

        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", guid);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetPowercfgPath(), request.FileName);
        Assert.Equal(new[] { "/getactivescheme" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), request.Timeout);
    }

    [Fact]
    public async Task TrySetPowerScheme_IssuesSetactiveWithGuid()
    {
        var executor = new RecordingCommandExecutor();
        var operations = new StandardGameBoostSystemOperations(executor);

        Assert.True(await operations.TrySetPowerSchemeAsync("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"));

        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetPowercfgPath(), request.FileName);
        Assert.Equal(new[] { "/setactive", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(2000), request.Timeout);
    }

    [Fact]
    public async Task PowerSchemeExists_MatchesGuidFromListOutput()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(
            "Existing Power Schemes: Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e (Balanced) " +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c (High performance)");
        var operations = new StandardGameBoostSystemOperations(executor);

        // GUID из /list, сравнение регистронезависимое; отсутствующий GUID — false
        Assert.True(await operations.PowerSchemeExistsAsync("8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C"));
        Assert.False(await operations.PowerSchemeExistsAsync("11111111-2222-3333-4444-555555555555"));

        Assert.Equal(2, executor.Requests.Count);
        Assert.All(executor.Requests, r => Assert.Equal(new[] { "/list" }, r.Arguments));
        Assert.All(executor.Requests, r => Assert.Equal(SystemToolLocator.GetPowercfgPath(), r.FileName));
    }

    // ==== День 24 — отмена не глотается как отказ утилиты (OCE-дисциплина, reviewer P3) ====

    [Fact]
    public async Task StopService_Cancellation_PropagatesOperationCanceled()
    {
        // День 24: отмена по токену вызывающего (OCE от исполнителя) —
        // исключение наружу, а не «-1 = служба не остановлена»: буст обязан
        // отличать отказ утилиты от отмены. Стаб эмулирует контракт реального
        // исполнителя (ProcessCommandExecutor бросает OCE при отмене ct)
        var executor = new RecordingCommandExecutor(_ => throw new OperationCanceledException());
        var operations = new StandardGameBoostSystemOperations(executor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            operations.StopServiceAsync("SysMain"));
    }
}
