using SmartCleaner.Core.Cleaning;
using System.Text;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 19 — срез C: реальный ProcessCommandExecutor с явной кодировкой
/// декодирования stdout (StandardOutputEncoding). Эмпирика на живых процессах:
/// дефолтное декодирование .NET = Console.OutputEncoding хоста (в UTF-8-консоли
/// OEM-вывод ломается; GUI-процесс без консоли получает «Codepage 0» = ANSI),
/// поэтому парсящие локализованный вывод сайты обязаны задавать кодировку явно.
/// Скрипт PowerShell принудительно пишет UTF-8 в пайп — детерминированно при
/// любой консоли хоста: если исполнитель игнорирует свойство запроса,
/// декодирование по дефолту хоста даёт mojibake и тест падает.
/// </summary>
public class ProcessCommandExecutorEncodingTests
{
    private static string WindowsPowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task ExecuteAsync_StandardOutputEncoding_DecodesUtf8ForcedOutput()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = WindowsPowerShellPath,
            Arguments =
            [
                "-NoProfile",
                "-NonInteractive",
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; Write-Output 'ПроверкаКодировки'"
            ],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(30),
            StandardOutputEncoding = Encoding.UTF8
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ПроверкаКодировки", result.StandardOutput);
    }

    [Fact]
    public async Task ExecuteAsync_NoExplicitEncoding_DefaultDecodingForAsciiOutput()
    {
        // Кодировка не задана — прежнее поведение срезов A/B (дефолт .NET);
        // ASCII-вывод не зависит от выбора кодировки.
        var executor = new ProcessCommandExecutor();

        var result = await executor.ExecuteAsync(new CommandExecutionRequest
        {
            FileName = WindowsPowerShellPath,
            Arguments = ["-NoProfile", "-NonInteractive", "Write-Output 'ascii-ok'"],
            WorkingDirectory = string.Empty,
            Timeout = TimeSpan.FromSeconds(30)
        });

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("ascii-ok", result.StandardOutput);
    }
}
