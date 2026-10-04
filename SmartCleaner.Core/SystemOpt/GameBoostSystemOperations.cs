using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using System.Text.RegularExpressions;
// UseWindowsForms тянет System.Windows.Forms.ICommandExecutor — снимаем
// неоднозначность в пользу контракта исполнителя команд
using ICommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

namespace SmartCleaner.Core.SystemOpt;

/// <summary>
/// Системные операции Game Boost: службы и план питания.
/// Абстракция позволяет тестировать логику буста без реального изменения системы.
/// День 17: операции асинхронны — исполняются через ICommandExecutor.
/// </summary>
public interface IGameBoostSystemOperations
{
    /// <summary>Остановить системную службу (net stop). true — служба остановлена.</summary>
    Task<bool> StopServiceAsync(string serviceName, CancellationToken ct = default);

    /// <summary>Запустить системную службу (net start). true — служба запущена.</summary>
    Task<bool> StartServiceAsync(string serviceName, CancellationToken ct = default);

    /// <summary>GUID активного плана питания (powercfg /getactivescheme). null — прочитать не удалось.</summary>
    Task<string?> GetActivePowerSchemeGuidAsync(CancellationToken ct = default);

    /// <summary>Активировать план питания (powercfg /setactive). true — команда выполнена успешно.</summary>
    Task<bool> TrySetPowerSchemeAsync(string schemeGuid, CancellationToken ct = default);

    /// <summary>Существует ли план питания с указанным GUID (powercfg /list).</summary>
    Task<bool> PowerSchemeExistsAsync(string schemeGuid, CancellationToken ct = default);
}

/// <summary>
/// Реальные системные операции через net.exe и powercfg — поверх эталонного
/// исполнителя команд (День 17, срез A).
/// </summary>
public sealed class StandardGameBoostSystemOperations : IGameBoostSystemOperations
{
    private static readonly TimeSpan ServiceTimeout = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PowerCfgTimeout = TimeSpan.FromMilliseconds(2000);
    private static readonly Regex PowerSchemeGuidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger _logger;

    /// <summary>
    /// Создаёт операции поверх реального исполнителя команд. Необязательный
    /// исполнитель — шов для детерминированных тестов: стаб фиксирует команды
    /// (полное имя утилиты, аргументы, таймаут). DI-регистрация — День 19.
    /// Необязательный ILogger — диагностика в Release (День 20).
    /// </summary>
    public StandardGameBoostSystemOperations(ICommandExecutor? commandExecutor = null, ILogger? logger = null)
    {
        _commandExecutor = commandExecutor ?? new ProcessCommandExecutor();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<bool> StopServiceAsync(string serviceName, CancellationToken ct = default) =>
        await RunCommandAsync(SystemToolLocator.GetNetPath(), ["stop", serviceName], ServiceTimeout, ct) == 0;

    public async Task<bool> StartServiceAsync(string serviceName, CancellationToken ct = default) =>
        await RunCommandAsync(SystemToolLocator.GetNetPath(), ["start", serviceName], ServiceTimeout, ct) == 0;

    public async Task<string?> GetActivePowerSchemeGuidAsync(CancellationToken ct = default)
    {
        var output = await RunCommandCaptureAsync(SystemToolLocator.GetPowercfgPath(), ["/getactivescheme"], PowerCfgTimeout, ct);
        if (output is null) return null;
        var match = PowerSchemeGuidRegex.Match(output);
        return match.Success ? match.Groups[0].Value.ToLowerInvariant() : null;
    }

    public async Task<bool> TrySetPowerSchemeAsync(string schemeGuid, CancellationToken ct = default) =>
        await RunCommandAsync(SystemToolLocator.GetPowercfgPath(), ["/setactive", schemeGuid], PowerCfgTimeout, ct) == 0;

    public async Task<bool> PowerSchemeExistsAsync(string schemeGuid, CancellationToken ct = default)
    {
        var output = await RunCommandCaptureAsync(SystemToolLocator.GetPowercfgPath(), ["/list"], PowerCfgTimeout, ct);
        if (output is null) return false;
        return PowerSchemeGuidRegex.Matches(output).Any(m =>
            string.Equals(m.Groups[0].Value, schemeGuid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Возвращает код выхода команды или -1 при ошибке/таймауте.</summary>
    private async Task<int> RunCommandAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.Empty,
                Timeout = timeout
            }, ct);
            // Таймаут — тоже отказ: никакого «успеха по коду -1»
            return result.TimedOut ? -1 : result.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // День 24: отмена по токену вызывающего (OCE от исполнителя) —
            // исключение наружу, а не «-1 = утилита отказала»: буст обязан
            // отличать отказ net/powercfg от отмены
            _logger.LogWarning(ex, "{FileName} {Arguments}: {Error}", fileName, string.Join(' ', arguments), ex.Message);
            return -1;
        }
    }

    /// <summary>Возвращает stdout команды или null при ошибке/таймауте.</summary>
    private async Task<string?> RunCommandCaptureAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = string.Empty,
                Timeout = timeout
            }, ct);
            if (result.TimedOut || result.ExitCode != 0)
            {
                return null;
            }
            return result.StandardOutput;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{FileName} {Arguments}: {Error}", fileName, string.Join(' ', arguments), ex.Message);
            return null;
        }
    }
}
