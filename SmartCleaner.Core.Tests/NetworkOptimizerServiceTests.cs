using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Network;
using Xunit;
using Xunit.Abstractions;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 17 — срез A (ROADMAP): контрактные тесты исполнителя команд для сети.
/// Стаб RecordingCommandExecutor фиксирует фактические команды: полное имя
/// утилиты (SystemToolLocator — binary planting M7), аргументы, таймаут.
/// ExitCode-гварды и честные причины ошибок — дисциплина дней 4-5.
/// </summary>
public class NetworkOptimizerServiceTests
{
    private readonly ITestOutputHelper _output;

    public NetworkOptimizerServiceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task FlushDnsAndResetWinsockAsync_IssuesIpconfigAndNetshThroughExecutor()
    {
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.FlushDnsAndResetWinsockAsync();

        Assert.True(result.Success);
        Assert.Equal(2, executor.Requests.Count);

        var flush = executor.Requests[0];
        Assert.Equal(SystemToolLocator.GetIpconfigPath(), flush.FileName);
        Assert.Equal(new[] { "/flushdns" }, flush.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), flush.Timeout);

        var arp = executor.Requests[1];
        Assert.Equal(SystemToolLocator.GetNetshPath(), arp.FileName);
        Assert.Equal(new[] { "interface", "ip", "delete", "arpcache" }, arp.Arguments);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), arp.Timeout);

        foreach (var request in executor.Requests)
        {
            _output.WriteLine(RecordingCommandExecutor.Describe(request));
        }
    }

    [Fact]
    public async Task FlushDnsAndResetWinsockAsync_FlushdnsFails_ReturnsFalseWithHonestMessage()
    {
        // ExitCode-гвард: отказ очистки кэша = отказ всей операции,
        // netsh (ARP) после него не вызывается; причина — в сообщении
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "flush failed");
        var service = new NetworkOptimizerService(executor);

        var result = await service.FlushDnsAndResetWinsockAsync();

        Assert.False(result.Success);
        Assert.Contains("код 1", result.Message);
        Assert.Contains("flush failed", result.Message);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task ApplyDnsAsync_IssuesSystemPowerShellWithScriptAndTimeout()
    {
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.ApplyDnsAsync("1.1.1.1", "1.0.0.1");

        Assert.True(result.Success);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetWindowsPowerShellPath(), request.FileName);
        Assert.Equal(new[] { "-NoProfile", "-NonInteractive", "-Command" }, request.Arguments.Take(3));
        var script = Assert.Single(request.Arguments.Skip(3));
        Assert.Contains("Set-DnsClientServerAddress", script);
        Assert.Contains("('1.1.1.1','1.0.0.1')", script);
        Assert.Contains("-ErrorAction Stop", script);
        Assert.Equal(TimeSpan.FromMilliseconds(15000), request.Timeout);

        _output.WriteLine(RecordingCommandExecutor.Describe(request));
    }

    [Fact]
    public async Task ApplyDnsAsync_PowerShellFails_ReturnsFalseWithHonestMessage()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "no active network adapters");
        var service = new NetworkOptimizerService(executor);

        var result = await service.ApplyDnsAsync("1.1.1.1", "1.0.0.1");

        Assert.False(result.Success);
        Assert.Contains("код 1", result.Message);
        Assert.Contains("no active network adapters", result.Message);
    }

    [Fact]
    public async Task ResetDnsToDhcpAsync_IssuesResetCommandThroughExecutor()
    {
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.ResetDnsToDhcpAsync();

        Assert.True(result.Success);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetWindowsPowerShellPath(), request.FileName);
        Assert.Equal(new[] { "-NoProfile", "-NonInteractive", "-Command" }, request.Arguments.Take(3));
        var script = Assert.Single(request.Arguments.Skip(3));
        Assert.Contains("-ResetServerAddresses", script);
        Assert.Contains("-ErrorAction Stop", script);
        Assert.Equal(TimeSpan.FromMilliseconds(15000), request.Timeout);
    }
}
