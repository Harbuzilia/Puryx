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

    // ==== День 21 — L3 (ROADMAP): отмена минутного профиля служб ====

    [Fact]
    public async Task ApplyProfileAsync_CancelledBeforeStart_ThrowsWithoutCommands()
    {
        // Токен уже отменён — профиль не должен запустить ни одной команды
        var executor = new RecordingCommandExecutor();
        var optimizer = new WindowsServicesOptimizer(executor);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => optimizer.ApplyProfileAsync(ServiceProfileType.Gaming, null, cts.Token));

        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task ApplyProfileAsync_CancellationDuringCommand_PropagatesAndStopsProfile()
    {
        // Отмена во время sc config первой службы: OperationCanceledException
        // от исполнителя обязана дойти до вызывающего (не проглатываться
        // catch(Exception) в SetServiceStartupAsync), профиль останавливается
        using var cts = new CancellationTokenSource();
        var executor = new RecordingCommandExecutor(_ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var optimizer = new WindowsServicesOptimizer(executor);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => optimizer.ApplyProfileAsync(ServiceProfileType.Gaming, null, cts.Token));

        // Отменяется «за секунды»: одна команда, новых не начато
        Assert.Single(executor.Requests);
        Assert.Equal(new[] { "config", "SysMain", "start=", "disabled" }, executor.Requests[0].Arguments);
    }

    [Fact]
    public async Task ApplyProfileAsync_CancelledBetweenIterations_StopsProfile()
    {
        // Отмена между итерациями: первая служба применена (config + net stop),
        // вторая не начата — ThrowIfCancellationRequested на витке цикла
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var executor = new RecordingCommandExecutor(_ =>
        {
            calls++;
            if (calls == 1) cts.Cancel();
            return new CommandExecutionResult { ExitCode = 0 };
        });
        var optimizer = new WindowsServicesOptimizer(executor);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => optimizer.ApplyProfileAsync(ServiceProfileType.Gaming, null, cts.Token));

        Assert.Equal(2, executor.Requests.Count);
        Assert.Equal(new[] { "config", "SysMain", "start=", "disabled" }, executor.Requests[0].Arguments);
        Assert.Equal(new[] { "stop", "SysMain", "/y" }, executor.Requests[1].Arguments);
    }

    // ==== День 22 — финализация: фейл sc ≠ успех (мутационный сюит P3) ====

    [Fact]
    public async Task SetServiceStartupAsync_ManualType_ScConfigFails_ReturnsFalseWithoutNetStop()
    {
        // ExitCode-гвард не зависит от типа запуска: для Manual после отказа
        // конфигурации net stop не вызывается вовсе (его и не должно быть)
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1060, "FAILED 1060: The specified service does not exist");
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Manual);

        Assert.False(result);
        Assert.Single(executor.Requests);
        Assert.Equal(new[] { "config", "PuryxStubService", "start=", "demand" }, executor.Requests[0].Arguments);
    }

    [Fact]
    public async Task SetServiceStartupAsync_NetStopFails_QueryFails_ReturnsFalseOnUnknownState()
    {
        // Отказ net stop + отказ sc query (состояние неизвестно): «цель
        // остановлена» не подтверждена — честный false, а не оптимистичный true
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(2, "The service did not respond to the control function.");
        executor.EnqueueFailure(1060, "FAILED 1060: The specified service does not exist");
        var optimizer = new WindowsServicesOptimizer(executor);

        var result = await optimizer.SetServiceStartupAsync("PuryxStubService", ServiceStartupType.Disabled);

        Assert.False(result);
        Assert.Equal(3, executor.Requests.Count);
        Assert.Equal(new[] { "query", "PuryxStubService" }, executor.Requests[2].Arguments);
    }
}
