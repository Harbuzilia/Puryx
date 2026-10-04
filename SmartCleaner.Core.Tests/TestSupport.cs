using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;
using System.Text.Json;

namespace SmartCleaner.Core.Tests;

internal sealed class InMemoryConfigService(IReadOnlyList<string> paths) : IConfigService
{
    public ConfigMode Mode => ConfigMode.Portable;
    public string ConfigDirectory => Path.GetTempPath();
    public string LogDirectory => Path.GetTempPath();
    public string UserRulesDirectory => Path.GetTempPath();

    public T Load<T>(string filename) where T : new()
    {
        if (typeof(T) == typeof(ScanPathsConfig))
        {
            return (T)(object)new ScanPathsConfig
            {
                Paths = paths.ToList()
            };
        }

        return new T();
    }

    public void Save<T>(string filename, T data)
    {
    }

    public bool Exists(string filename) => false;
}

/// <summary>
/// Конфиг-сервис на реальных файлах в изолированном каталоге — для тестов,
/// которым нужно RoundTrip-поведение Load/Save (ключи, состояние).
/// </summary>
internal sealed class FileBackedConfigService : IConfigService
{
    private readonly string _configDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public FileBackedConfigService(string configDirectory)
    {
        _configDirectory = configDirectory;
        // Как и реальный ConfigService — каталог обязан существовать
        Directory.CreateDirectory(_configDirectory);
    }

    public string ConfigDirectory => _configDirectory;
    public ConfigMode Mode => ConfigMode.Portable;
    public string LogDirectory => _configDirectory;
    public string UserRulesDirectory => _configDirectory;

    public T Load<T>(string filename) where T : new()
    {
        var path = Path.Combine(_configDirectory, filename);
        if (!File.Exists(path)) return new T();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions) ?? new T();
    }

    public void Save<T>(string filename, T data) =>
        File.WriteAllText(Path.Combine(_configDirectory, filename), JsonSerializer.Serialize(data));

    public bool Exists(string filename) => File.Exists(Path.Combine(_configDirectory, filename));
}

internal sealed class AllowAllSafetyService : ISafetyService
{
    public TimeSpan ProtectedPeriod { get; set; }

    public bool IsWhitelisted(string path) => false;
    public void AddToWhitelist(string pattern) { }
    public void RemoveFromWhitelist(string pattern) { }
    public IEnumerable<string> GetWhitelistPatterns() => [];
    public bool IsWithinProtectedPeriod(string path) => false;
    public bool IsFileLocked(string path) => false;
    public void SaveWhitelist() { }
    public void LoadWhitelist() { }

    public DeleteValidation ValidateForDeletion(ScannedItem item)
    {
        return new DeleteValidation
        {
            CanDelete = true,
            RequiresElevation = false
        };
    }
}

internal sealed class BlockingSafetyService : ISafetyService
{
    public TimeSpan ProtectedPeriod { get; set; }

    public bool IsWhitelisted(string path) => true;
    public void AddToWhitelist(string pattern) { }
    public void RemoveFromWhitelist(string pattern) { }
    public IEnumerable<string> GetWhitelistPatterns() => ["**"];
    public bool IsWithinProtectedPeriod(string path) => false;
    public bool IsFileLocked(string path) => false;
    public void SaveWhitelist() { }
    public void LoadWhitelist() { }

    public DeleteValidation ValidateForDeletion(ScannedItem item)
    {
        return new DeleteValidation
        {
            CanDelete = false,
            BlockReason = "Заблокировано тестовым фейком"
        };
    }
}

/// <summary>
/// Стаб ICommandExecutor для сервисных тестов (День 17): записывает каждый
/// запрос — полное имя утилиты, аргументы, таймаут — и возвращает управляемые
/// результаты. Результаты задаются либо сценарием-очередью (Enqueue* — по
/// одному результату на вызов, для последовательностей вида
/// sc config → net stop → sc query), либо функцией-обработчиком;
/// очередь приоритетнее.
/// </summary>
internal sealed class RecordingCommandExecutor : ICommandExecutor
{
    private readonly Func<CommandExecutionRequest, CommandExecutionResult> _handler;
    private readonly Queue<CommandExecutionResult> _scriptedResults = new();

    public RecordingCommandExecutor(Func<CommandExecutionRequest, CommandExecutionResult>? handler = null)
    {
        _handler = handler ?? (_ => new CommandExecutionResult { ExitCode = 0 });
    }

    /// <summary>Все выполненные запросы в порядке поступления.</summary>
    public List<CommandExecutionRequest> Requests { get; } = [];

    /// <summary>Запланировать результат следующего ExecuteAsync (по одному на вызов).</summary>
    public void EnqueueResult(CommandExecutionResult result) => _scriptedResults.Enqueue(result);

    /// <summary>Запланировать успешный (exit 0) результат со stdout.</summary>
    public void EnqueueSuccess(string standardOutput = "") =>
        _scriptedResults.Enqueue(new CommandExecutionResult { ExitCode = 0, StandardOutput = standardOutput });

    /// <summary>Запланировать неуспешный результат с exitCode и stderr.</summary>
    public void EnqueueFailure(int exitCode, string standardError = "") =>
        _scriptedResults.Enqueue(new CommandExecutionResult { ExitCode = exitCode, StandardError = standardError });

    public Task<CommandExecutionResult> ExecuteAsync(CommandExecutionRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        var result = _scriptedResults.Count > 0 ? _scriptedResults.Dequeue() : _handler(request);
        return Task.FromResult(result);
    }

    /// <summary>Человекочитаемая запись команды (полное имя, аргументы, таймаут) для вывода теста и сообщений ассертов.</summary>
    public static string Describe(CommandExecutionRequest request) =>
        $"{request.FileName} {string.Join(' ', request.Arguments)} (таймаут {request.Timeout.TotalMilliseconds:0} мс)";
}
