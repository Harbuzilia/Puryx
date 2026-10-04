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

    // ==== День 22 — финализация: фейл ipconfig/netsh/powershell ≠ успех ====

    [Fact]
    public async Task FlushDnsAndResetWinsockAsync_ArpResetFails_ReturnsFalseWithHonestMessage()
    {
        // Очистка DNS прошла, сброс ARP-таблицы — нет: успех всей операции
        // невозможен, причина — в сообщении
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess();
        executor.EnqueueFailure(1, "arp reset failed");
        var service = new NetworkOptimizerService(executor);

        var result = await service.FlushDnsAndResetWinsockAsync();

        Assert.False(result.Success);
        Assert.Contains("код 1", result.Message);
        Assert.Contains("ARP", result.Message);
        Assert.Contains("arp reset failed", result.Message);
        Assert.Equal(2, executor.Requests.Count);
    }

    [Fact]
    public async Task FlushDnsAndResetWinsockAsync_FlushdnsFailsWithStdoutOnlyError_StdoutInMessage()
    {
        // Утилита с текстом ошибки в stdout (пустой stderr): ToolOutput обязан
        // показать фактический вывод, а не пустую причину
        var executor = new RecordingCommandExecutor();
        executor.EnqueueResult(new CommandExecutionResult
        {
            ExitCode = 1,
            StandardOutput = "Access is denied (stdout)"
        });
        var service = new NetworkOptimizerService(executor);

        var result = await service.FlushDnsAndResetWinsockAsync();

        Assert.False(result.Success);
        Assert.Contains("код 1", result.Message);
        Assert.Contains("Access is denied (stdout)", result.Message);
        Assert.Single(executor.Requests);
    }

    [Fact]
    public async Task ResetDnsToDhcpAsync_PowerShellFails_ReturnsFalseWithHonestMessage()
    {
        // -ErrorAction Stop: отказ Set-DnsClientServerAddress = ненулевой код;
        // сброс на DHCP обязан честно провалиться
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "no active network adapters");
        var service = new NetworkOptimizerService(executor);

        var result = await service.ResetDnsToDhcpAsync();

        Assert.False(result.Success);
        Assert.Contains("код 1", result.Message);
        Assert.Contains("no active network adapters", result.Message);
    }

    // ==== День 24 — валидация формата DNS на входе публичного API (reviewer P3) ====

    [Fact]
    public async Task ApplyDnsAsync_InvalidPrimaryDnsFormat_RejectedBeforeScript()
    {
        // Пресеты UI безопасны, но API публичный: значения интерполируются в
        // PowerShell-скрипт — формат обязан проверяться на входе, произвольная
        // строка до скрипта не доходит
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.ApplyDnsAsync("'; Remove-Item -Recurse C:", "1.0.0.1");

        Assert.False(result.Success);
        Assert.Contains("Недопустимый", result.Message);
        Assert.Empty(executor.Requests); // команда не выполнялась вовсе
    }

    [Fact]
    public async Task ApplyDnsAsync_InvalidSecondaryDnsFormat_RejectedBeforeScript()
    {
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.ApplyDnsAsync("1.1.1.1", "not-an-ip");

        Assert.False(result.Success);
        Assert.Contains("Недопустимый", result.Message);
        Assert.Empty(executor.Requests);
    }

    [Theory]
    [InlineData("8.8.8.8", "8.8.4.4")]
    [InlineData("2001:4860:4860::8888", "2001:4860:4860::8844")]
    public async Task ApplyDnsAsync_ValidAddresses_IssuedThroughExecutor(string primary, string secondary)
    {
        // Валидные IPv4/IPv6 проходят гвард и попадают в скрипт
        var executor = new RecordingCommandExecutor();
        var service = new NetworkOptimizerService(executor);

        var result = await service.ApplyDnsAsync(primary, secondary);

        Assert.True(result.Success);
        var request = Assert.Single(executor.Requests);
        Assert.Contains(primary, Assert.Single(request.Arguments.Skip(3)));
        Assert.Contains(secondary, Assert.Single(request.Arguments.Skip(3)));
    }
}
