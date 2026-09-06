namespace SmartCleaner.Core.Cleaning;

public record CommandExecutionRequest
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string WorkingDirectory { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
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
