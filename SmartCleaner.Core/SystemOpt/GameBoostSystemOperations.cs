using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartCleaner.Core.SystemOpt;

/// <summary>
/// Системные операции Game Boost: службы и план питания.
/// Абстракция позволяет тестировать логику буста без реального изменения системы.
/// </summary>
public interface IGameBoostSystemOperations
{
    /// <summary>Остановить системную службу (net stop). true — служба остановлена.</summary>
    bool StopService(string serviceName);

    /// <summary>Запустить системную службу (net start). true — служба запущена.</summary>
    bool StartService(string serviceName);

    /// <summary>GUID активного плана питания (powercfg /getactivescheme). null — прочитать не удалось.</summary>
    string? GetActivePowerSchemeGuid();

    /// <summary>Активировать план питания (powercfg /setactive). true — команда выполнена успешно.</summary>
    bool TrySetPowerScheme(string schemeGuid);

    /// <summary>Существует ли план питания с указанным GUID (powercfg /list).</summary>
    bool PowerSchemeExists(string schemeGuid);
}

/// <summary>
/// Реальные системные операции через net.exe и powercfg.
/// </summary>
public sealed class StandardGameBoostSystemOperations : IGameBoostSystemOperations
{
    private const int ServiceTimeoutMs = 1500;
    private const int PowerCfgTimeoutMs = 2000;
    private static readonly Regex PowerSchemeGuidRegex = new(
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    public bool StopService(string serviceName) =>
        RunCommand("net.exe", $"stop {serviceName}", ServiceTimeoutMs) == 0;

    public bool StartService(string serviceName) =>
        RunCommand("net.exe", $"start {serviceName}", ServiceTimeoutMs) == 0;

    public string? GetActivePowerSchemeGuid()
    {
        var output = RunCommandCapture("powercfg", "/getactivescheme", PowerCfgTimeoutMs);
        if (output is null) return null;
        var match = PowerSchemeGuidRegex.Match(output);
        return match.Success ? match.Groups[0].Value.ToLowerInvariant() : null;
    }

    public bool TrySetPowerScheme(string schemeGuid) =>
        RunCommand("powercfg", $"/setactive {schemeGuid}", PowerCfgTimeoutMs) == 0;

    public bool PowerSchemeExists(string schemeGuid)
    {
        var output = RunCommandCapture("powercfg", "/list", PowerCfgTimeoutMs);
        if (output is null) return false;
        return PowerSchemeGuidRegex.Matches(output).Any(m =>
            string.Equals(m.Groups[0].Value, schemeGuid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Возвращает код выхода команды или -1 при ошибке/таймауте.</summary>
    private static int RunCommand(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (proc is null || !proc.WaitForExit(timeoutMs)) return -1;
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoostSystemOperations] {fileName} {arguments}: {ex.Message}");
            return -1;
        }
    }

    /// <summary>Возвращает stdout команды или null при ошибке/таймауте.</summary>
    private static string? RunCommandCapture(string fileName, string arguments, int timeoutMs)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            });
            if (proc is null) return null;

            // Чтение асинхронно с таймаутом, чтобы зависший процесс не заблокировал поток навсегда
            var readTask = proc.StandardOutput.ReadToEndAsync();
            if (!proc.WaitForExit(timeoutMs) || !readTask.Wait(timeoutMs)) return null;
            return readTask.Result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameBoostSystemOperations] {fileName} {arguments}: {ex.Message}");
            return null;
        }
    }
}
