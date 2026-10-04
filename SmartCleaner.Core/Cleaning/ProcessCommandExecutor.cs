using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text;

namespace SmartCleaner.Core.Cleaning;

public sealed class ProcessCommandExecutor : ICommandExecutor
{
    private readonly ILogger _logger;

    /// <summary>
    /// Необязательный логгер — шов для диагностики в Release (День 20).
    /// </summary>
    public ProcessCommandExecutor(ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(request.Timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // Явная кодировка запроса (День 19, срез C) побеждает неопределённый
            // дефолт .NET; null — прежнее поведение срезов A/B
            StandardOutputEncoding = request.StandardOutputEncoding
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new CommandExecutionResult
            {
                ExitCode = -1,
                StandardError = "Не удалось запустить процесс"
            };
        }

        // Построчный насос stdout включается только при запросе стриминга
        // (День 18, срез B — compact.exe): без запроса — прежнее чтение
        // целиком (срез A)
        Task<string> stdoutTask = request.StandardOutputLineProgress is null
            ? process.StandardOutput.ReadToEndAsync(linkedCts.Token)
            : PumpStandardOutputLinesAsync(process, request.StandardOutputLineProgress);
        var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
            return new CommandExecutionResult
            {
                ExitCode = process.ExitCode,
                StandardOutput = await stdoutTask,
                StandardError = await stderrTask
            };
        }
        catch (OperationCanceledException)
        {
            // Таймаут ИЛИ отмена вызывающего: в обоих случаях процесс обязан
            // быть убит (kill-tree) — иначе долгая утилита (compact.exe до часа)
            // продолжит работать в фоне после «отмены» (День 18, срез B —
            // прежде при отмене вызывающего процесс оставался жить)
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kill process tree on timeout/cancellation failed: {Error}", ex.Message);
            }

            // Отмена по токену вызывающего — не «результат», а исключение:
            // вызывающий код обязан отличить её от таймаута (TimedOut)
            if (ct.IsCancellationRequested)
                throw;

            return new CommandExecutionResult
            {
                ExitCode = -1,
                TimedOut = true,
                StandardError = "Превышен таймаут выполнения"
            };
        }
    }

    // Читает stdout построчно, транслируя каждую непустую строку в progress
    // и накапливая полный текст — он нужен в StandardOutput (потребители
    // используют вывод как причину сбоя). Фильтрация строк — на потребителе.
    // Насос без токена: завершение гарантирует kill-tree при таймауте/отмене
    // (закрытие пайпа -> EOF -> null).
    private static async Task<string> PumpStandardOutputLinesAsync(Process process, IProgress<string> progress)
    {
        var all = new StringBuilder();
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line is null) break;

            all.AppendLine(line);
            if (line.Length > 0)
                progress.Report(line);
        }
        return all.ToString();
    }
}
