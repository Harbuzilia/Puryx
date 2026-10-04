using SmartCleaner.Core.Cleaning;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 18 — срез B: реальный ProcessCommandExecutor с построчным
/// прогрессом stdout (стриминг для compact.exe) и дисциплиной
/// таймаута/отмены. Живые процессы cmd.exe — детерминированный вывод,
/// без изменений системы (как живой sc.exe в WindowsServicesOptimizerTests).
/// </summary>
public class ProcessCommandExecutorProgressTests
{
    private sealed class CollectingProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    private static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task ExecuteAsync_WithLineProgress_ReportsLinesAndReturnsFullOutput()
    {
        var lines = new List<string>();
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = CmdPath,
            Arguments = ["/c", "echo one & echo two"],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(15),
            StandardOutputLineProgress = new CollectingProgress(lines)
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(lines, l => l.Contains("one"));
        Assert.Contains(lines, l => l.Contains("two"));
        Assert.Contains("one", result.StandardOutput);
        Assert.Contains("two", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutLineProgress_ReturnsOutput()
    {
        // Прогресс не запрошен — прежнее поведение (срез A): полный вывод, без колбэков
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = CmdPath,
            Arguments = ["/c", "echo three"],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(15)
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("three", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_ReturnsTimedOutResult()
    {
        // 30-секундный ping под таймаутом 2 с — честный TimedOut-результат
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = CmdPath,
            Arguments = ["/c", "ping -n 30 127.0.0.1 > nul"],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(2)
        });

        Assert.Equal(-1, result.ExitCode);
        Assert.True(result.TimedOut);
        Assert.Equal("Превышен таймаут выполнения", result.StandardError);
    }

    [Fact]
    public async Task ExecuteAsync_UserCancellation_ThrowsOperationCanceled()
    {
        // Отмена вызывающего — исключение (не «результат»): вызывающий обязан
        // отличить её от таймаута; kill-tree внутри исполнителя обязан снять
        // долгий процесс, иначе compact.exe продолжит работать после «отмены»
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var executor = new ProcessCommandExecutor();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteAsync(new CommandExecutionRequest
            {
                FileName = CmdPath,
                Arguments = ["/c", "ping -n 30 127.0.0.1 > nul"],
                WorkingDirectory = string.Empty,
                Timeout = TimeSpan.FromMinutes(5)
            }, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_StreamingOutputUnderTimeout_HonestTimedOutWithoutException()
    {
        // День 24 (reviewer P3): таймаут в середине потокового вывода — задачи
        // чтения потоков стоят на linkedCts.Token, StreamReader может сорваться
        // в TaskCanceledException; результат обязан быть честным TimedOut,
        // а не исключение наружу. ping печатает строку в секунду — вывод
        // гарантированно идёт в момент таймаута
        var lines = new List<string>();
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = CmdPath,
            Arguments = ["/c", "ping -n 30 127.0.0.1"],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(2),
            StandardOutputLineProgress = new CollectingProgress(lines)
        });

        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("Превышен таймаут выполнения", result.StandardError);
        Assert.True(lines.Count > 0); // вывод действительно стримился — таймаут в середине
    }
}
