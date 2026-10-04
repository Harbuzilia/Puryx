using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Compression;
using SmartCleaner.Core.Helpers;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 18 — срез B: контрактные тесты compact.exe через стаб
/// RecordingCommandExecutor. Стаб фиксирует полное имя утилиты
/// (SystemToolLocator), аргументы (квотинг пути с пробелами —
/// ответственность ArgumentList), таймаут и подключение построчного
/// прогресс-насоса (стриминг stdout: проценты сжатия видны в реальном
/// времени, а не после завершения). Таймаут/ненулевой exit/отмена —
/// честные сообщения, сохранённые из прежней реализации.
/// </summary>
public class CompactEngineTests
{
    private sealed class CollectingProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "puryx-compact-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task CompressDirectoryAsync_IssuesCompactThroughExecutor_WithProgressPump()
    {
        var dir = CreateTempDirectory();
        try
        {
            var executor = new RecordingCommandExecutor();
            executor.EnqueueSuccess("123 : 100%");
            var engine = new CompactEngine(executor);

            var result = await engine.CompressDirectoryAsync(dir, "LZX", new CollectingProgress([]));

            Assert.True(result.Success);
            var request = Assert.Single(executor.Requests);
            Assert.Equal(SystemToolLocator.GetCompactPath(), request.FileName);
            Assert.Equal(new[] { "/c", $"/s:{dir}", "/exe:lzx", "/i", "/f" }, request.Arguments);
            Assert.Equal(TimeSpan.FromHours(1), request.Timeout);
            Assert.NotNull(request.StandardOutputLineProgress);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task CompressDirectoryAsync_Xpress16KAlgorithm_MapsArgument()
    {
        var dir = CreateTempDirectory();
        try
        {
            var executor = new RecordingCommandExecutor();
            executor.EnqueueSuccess("");
            var engine = new CompactEngine(executor);

            var result = await engine.CompressDirectoryAsync(dir, "XPRESS16K");

            Assert.True(result.Success);
            var request = Assert.Single(executor.Requests);
            Assert.Equal(new[] { "/c", $"/s:{dir}", "/exe:xpress16k", "/i", "/f" }, request.Arguments);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task DecompressDirectoryAsync_IssuesCompactThroughExecutor_WithoutProgress()
    {
        var dir = CreateTempDirectory();
        try
        {
            var executor = new RecordingCommandExecutor();
            executor.EnqueueSuccess("1 file");
            var engine = new CompactEngine(executor);

            var result = await engine.DecompressDirectoryAsync(dir);

            Assert.True(result.Success);
            Assert.Equal("Распаковка успешно завершена!", result.Message);
            var request = Assert.Single(executor.Requests);
            Assert.Equal(SystemToolLocator.GetCompactPath(), request.FileName);
            Assert.Equal(new[] { "/u", $"/s:{dir}", "/i" }, request.Arguments);
            Assert.Equal(TimeSpan.FromHours(1), request.Timeout);
            Assert.Null(request.StandardOutputLineProgress);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task CompressDirectoryAsync_Timeout_FailureWithTimeoutMessage()
    {
        var dir = CreateTempDirectory();
        try
        {
            var executor = new RecordingCommandExecutor();
            executor.EnqueueResult(new CommandExecutionResult { ExitCode = -1, TimedOut = true });
            var engine = new CompactEngine(executor);

            var result = await engine.CompressDirectoryAsync(dir, "LZX");

            Assert.False(result.Success);
            Assert.Equal("«compact.exe» не завершилась за 60 мин", result.Message);
            Assert.Equal(0, result.SavedBytes);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task CompressDirectoryAsync_NonZeroExit_CompactFailureMessage()
    {
        var dir = CreateTempDirectory();
        try
        {
            var executor = new RecordingCommandExecutor();
            executor.EnqueueFailure(2, "file1.dat : отказано в доступе");
            var engine = new CompactEngine(executor);

            var result = await engine.CompressDirectoryAsync(dir, "LZX");

            Assert.False(result.Success);
            Assert.Equal(0, result.SavedBytes);
            Assert.Contains("кодом 2", result.Message);
            Assert.Contains("отказано в доступе", result.Message);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task CompressDirectoryAsync_Cancellation_ReturnsCancelledMessage()
    {
        var dir = CreateTempDirectory();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            // Исполнитель, падающий отменой — как реальный при user-cancel
            // (отмена токена вызывающего возвращается исключением, не результатом)
            var executor = new RecordingCommandExecutor(_ => throw new OperationCanceledException(cts.Token));
            var engine = new CompactEngine(executor);

            var result = await engine.CompressDirectoryAsync(dir, "LZX", ct: cts.Token);

            Assert.False(result.Success);
            Assert.Equal("Операция отменена пользователем", result.Message);
            Assert.Equal(0, result.SavedBytes);
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public void ProgressFilter_ForwardsOnlyPercentageLines_Trimmed()
    {
        // Исполнитель транслирует все непустые строки; пользователю нужны
        // только строки с процентами (поведение прежнего насоса)
        var forwarded = new List<string>();
        var filter = new CompactEngine.CompactProgressFilter(new CollectingProgress(forwarded));

        filter.Report("  12345 : 100%  ");
        filter.Report("  somefile.dat");
        filter.Report("   ");

        var line = Assert.Single(forwarded);
        Assert.Equal("12345 : 100%", line);
    }
}
