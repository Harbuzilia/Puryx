using System.Text;

namespace SmartCleaner.Core.Cleaning;

public record CommandExecutionRequest
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Построчный прогресс stdout: каждая непустая строка стандартного потока
    /// передаётся по мере поступления (стриминг долгих операций вроде
    /// compact.exe, печатающей строку на каждый файл). null — stdout читается
    /// целиком без стриминга (поведение по умолчанию, срез A). Полный текст
    /// вывода в любом случае возвращается в StandardOutput.
    /// </summary>
    public IProgress<string>? StandardOutputLineProgress { get; init; }

    /// <summary>
    /// Явная кодировка декодирования stdout (День 19, срез C — расширение по
    /// образцу стриминга Дня 18). Консольные утилиты Windows пишут в OEM/ANSI-
    /// странице, а дефолт .NET не определён: он равен Console.OutputEncoding
    /// хоста — в UTF-8-консоли OEM-вывод превращается в mojibake, GUI-процесс
    /// без консоли получает «Codepage 0» = ANSI (живой замер, День 19). null —
    /// дефолт .NET (поведение срезов A/B, сайты без локализованного парсинга).
    /// Потребители: schtasks (OEM — День 8), pnputil (ANSI).
    /// </summary>
    public Encoding? StandardOutputEncoding { get; init; }
}

public record CommandExecutionResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
}

public interface ICommandExecutor
{
    Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, CancellationToken ct = default);
}
