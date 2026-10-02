using System.Diagnostics;

namespace SmartCleaner.Core.Cleaning;

public sealed class ProcessCommandExecutor : ICommandExecutor
{
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
            CreateNoWindow = true
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

        var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
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
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProcessCommandExecutor] Kill process tree on timeout failed: {ex.Message}");
            }

            return new CommandExecutionResult
            {
                ExitCode = -1,
                TimedOut = true,
                StandardError = "Превышен таймаут выполнения"
            };
        }
    }
}
