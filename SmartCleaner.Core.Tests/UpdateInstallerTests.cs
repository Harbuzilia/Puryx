using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using SmartCleaner.Core.Update;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты UpdateInstaller (ROADMAP v3.1, День 12, фаза A): скачивание,
/// SHA256-верификация и rename-swap с рестартом — на фантомных файлах в
/// temp-каталоге теста, реальный exe НЕ затрагивается. Скачивание — через
/// стаб HttpMessageHandler (шов конструктора, паттерн UpdateCheckerTests);
/// Process.Start/Environment.Exit — мок IUpdateProcessLauncher (реальный
/// рестарт не выполняется). Сценарии: стриминг-скачивание с монотонным
/// прогрессом и точным контентом temp-файла; отказ при не-HTTPS ДО сети
/// (запрос не отправляется); HTTP-ошибка/сетевой сбой → Update_DownloadFailed
/// с вычисткой мусора temp; отмена вызывающего пробрасывается и чистит
/// partial; верный хэш → true (файл сохранён), мисматш → false + temp
/// удалён; swap-семантика: текущий exe → .old рядом (переживший .old
/// замещается), новый на место, запуск нового + Exit(0); отказ без прав
/// записи в каталог программы (ACL-deny) → Update_NoWriteAccess без
/// запуска и без повреждения файлов; отсутствие temp-файла →
/// Update_ApplyFailed с откатом exe на место; очистка .old при старте.
/// </summary>
public class UpdateInstallerTests : IDisposable
{
    private const string AssetUrl =
        "https://github.com/Harbuzilia/Puryx/releases/download/v3.1.0/Puryx-3.1.0-portable-win-x64.exe";

    private readonly string _root;
    private readonly string _appDir;
    private readonly string _tempDir;
    private readonly string _exePath;

    public UpdateInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PuryxUpdateInstallerTests_" + Guid.NewGuid().ToString("N"));
        _appDir = Path.Combine(_root, "app");
        _tempDir = Path.Combine(_root, "download");
        _exePath = Path.Combine(_appDir, "Puryx.exe");
        Directory.CreateDirectory(_appDir);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        TryDeleteTree(_root);
    }

    // ─── Скачивание ───

    [Fact]
    public async Task Download_WritesTempFileWithContent_ReportsMonotonicProgress()
    {
        // ~200 КБ против буфера 81920: три чанка чтения — промежуточные значения прогресса
        var payload = new byte[200_000];
        new Random(42).NextBytes(payload);
        var handler = new StubHandler(_ => Ok(payload));
        var installer = CreateInstaller(handler);
        var progress = new ProgressCollector();

        var result = await installer.DownloadAsync(UpdateWithSha(AssetUrl, Sha256Of(payload)), progress);

        Assert.Null(result.ErrorKey);
        Assert.True(result.Success);
        Assert.Equal(AssetUrl, handler.RequestUri);
        Assert.StartsWith("Puryx-Updater/", handler.UserAgent);
        // temp-файл в шове-каталоге, имя детерминировано версией релиза
        Assert.StartsWith(_tempDir, result.TempFilePath);
        Assert.EndsWith("Puryx-update-3.1.0.exe", result.TempFilePath);
        Assert.Equal(payload, await File.ReadAllBytesAsync(result.TempFilePath));
        // прогресс: в диапазоне 0..1, есть промежуточное значение, неубывающий, финал — 1.0
        Assert.NotEmpty(progress.Values);
        Assert.All(progress.Values, v => Assert.InRange(v, 0.0, 1.0));
        Assert.Contains(progress.Values, v => v > 0.0 && v < 1.0);
        Assert.Equal(1.0, progress.Values[^1], precision: 5);
        for (var i = 1; i < progress.Values.Count; i++)
        {
            Assert.True(progress.Values[i] >= progress.Values[i - 1],
                $"Прогресс не монотонный: [{i-1}]={progress.Values[i - 1]} [{i}]={progress.Values[i]}");
        }
    }

    [Fact]
    public async Task Download_HttpErrorStatus_FailsHonest_NoTempLeft()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadAsync(UpdateWithSha(AssetUrl, Sha256Of([1, 2, 3])));

        Assert.False(result.Success);
        Assert.Equal("Update_DownloadFailed", result.ErrorKey);
        Assert.Equal(string.Empty, result.TempFilePath);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Theory]
    [InlineData("http://github.com/Harbuzilia/Puryx/releases/download/v3.1.0/Puryx-3.1.0-portable-win-x64.exe")]
    [InlineData("ftp://example.com/Puryx-3.1.0-portable-win-x64.exe")]
    [InlineData("not-a-valid-uri")]
    public async Task Download_NonHttpsUrl_Refused_NoRequestSent(string url)
    {
        // Только HTTPS: отказ ДО любого сетевого запроса (стаб не вызывался)
        var handler = new StubHandler(_ => Ok([1]));
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadAsync(UpdateWithSha(url, Sha256Of([1])));

        Assert.False(result.Success);
        Assert.Equal("Update_NotHttps", result.ErrorKey);
        Assert.Null(handler.RequestUri);
        Assert.Empty(Directory.GetFiles(_tempDir));
    }

    [Fact]
    public async Task Download_NetworkFailure_DeletesStaleTempGarbage()
    {
        // Мусор от прошлой попытки в temp не переживает сбой скачивания
        var stalePath = Path.Combine(_tempDir, "Puryx-update-3.1.0.exe");
        File.WriteAllText(stalePath, "STALE-GARBAGE");
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var installer = CreateInstaller(handler);

        var result = await installer.DownloadAsync(UpdateWithSha(AssetUrl, Sha256Of([9])));

        Assert.False(result.Success);
        Assert.Equal("Update_DownloadFailed", result.ErrorKey);
        Assert.False(File.Exists(stalePath));
    }

    [Fact]
    public async Task Download_UserCancellation_Rethrows_AndCleansTemp()
    {
        // Отмена инициирована вызывающим кодом — пробрасывается (паттерн UpdateChecker),
        // partial-файл в temp вычищается
        var stalePath = Path.Combine(_tempDir, "Puryx-update-3.1.0.exe");
        File.WriteAllText(stalePath, "STALE-GARBAGE");
        var handler = new StubHandler(_ => Ok([1]));
        var installer = CreateInstaller(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => installer.DownloadAsync(UpdateWithSha(AssetUrl, Sha256Of([1])), null, cts.Token));

        Assert.False(File.Exists(stalePath));
    }

    // ─── Верификация SHA256 ───

    [Fact]
    public void Verify_MatchingSha256_True_FileKept()
    {
        var payload = "PHANTOM-NEW-EXE-CONTENT"u8.ToArray();
        var tempPath = Path.Combine(_tempDir, "verify-target.exe");
        File.WriteAllBytes(tempPath, payload);
        var installer = CreateInstaller();
        var sha = Sha256Of(payload);

        // hex в верхнем регистре (формат Convert.ToHexString) и в нижнем — оба валидны
        Assert.True(installer.VerifySha256(tempPath, sha));
        Assert.True(installer.VerifySha256(tempPath, sha.ToLowerInvariant()));
        Assert.True(File.Exists(tempPath));
    }

    [Fact]
    public void Verify_MismatchedSha256_False_TempDeleted()
    {
        var payload = "PHANTOM-NEW-EXE-CONTENT"u8.ToArray();
        var tempPath = Path.Combine(_tempDir, "verify-target.exe");
        File.WriteAllBytes(tempPath, payload);
        var installer = CreateInstaller();

        var result = installer.VerifySha256(tempPath, new string('D', 64));

        Assert.False(result);
        // Мисматч: невалидная загрузка не хранится в temp
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public void Verify_MissingTempFile_False_NoException()
    {
        var installer = CreateInstaller();
        var missing = Path.Combine(_tempDir, "no-such-file.exe");

        var result = installer.VerifySha256(missing, new string('A', 64));

        Assert.False(result);
    }

    // ─── Применение: rename-swap + рестарт ───

    [Fact]
    public void Apply_Swap_RenamesCurrentToOld_MovesNewInPlace_StartsAndExits()
    {
        File.WriteAllText(_exePath, "OLD-EXE-BYTES");
        var newExe = Path.Combine(_root, "downloaded-new.exe");
        File.WriteAllText(newExe, "NEW-EXE-BYTES");
        var oldPath = Path.ChangeExtension(_exePath, ".old");
        var (installer, launcher) = CreateApplier();

        var result = installer.ApplyUpdate(newExe);

        Assert.True(result.Success);
        Assert.Null(result.ErrorKey);
        // Текущий exe → .old рядом, содержимое сохранено
        Assert.Equal("OLD-EXE-BYTES", File.ReadAllText(oldPath));
        // Новый exe занял место текущего
        Assert.Equal("NEW-EXE-BYTES", File.ReadAllText(_exePath));
        // Скачанный файл перенесён (Move), не скопирован
        Assert.False(File.Exists(newExe));
        // Рестарт: запущен новый exe, текущий вышел кодом 0
        Assert.Equal(_exePath, launcher.StartedPath);
        Assert.Equal(0, launcher.ExitCode);
    }

    [Fact]
    public void Apply_StaleOldFile_ReplacedByCurrentExe()
    {
        // Переживший .old от прошлой попытки не блокирует swap — замещается текущим exe
        var oldPath = Path.ChangeExtension(_exePath, ".old");
        File.WriteAllText(oldPath, "ANCIENT-LEFTOVER");
        File.WriteAllText(_exePath, "OLD-EXE-BYTES");
        var newExe = Path.Combine(_root, "downloaded-new.exe");
        File.WriteAllText(newExe, "NEW-EXE-BYTES");
        var (installer, _) = CreateApplier();

        var result = installer.ApplyUpdate(newExe);

        Assert.True(result.Success);
        Assert.Equal("OLD-EXE-BYTES", File.ReadAllText(oldPath));
        Assert.Equal("NEW-EXE-BYTES", File.ReadAllText(_exePath));
    }

    [Fact]
    public void Apply_DeniedDirectoryWrite_NoWriteAccess_NothingTouched()
    {
        // Симуляция Program Files без прав: deny на создание/удаление записей
        // в каталоге программы — честный отказ с подсказкой, БЕЗ элевации
        File.WriteAllText(_exePath, "OLD-EXE-BYTES");
        var newExe = Path.Combine(_root, "downloaded-new.exe");
        File.WriteAllText(newExe, "NEW-EXE-BYTES");
        var (installer, launcher) = CreateApplier();

        var dirInfo = new DirectoryInfo(_appDir);
        var deny = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.DeleteSubdirectoriesAndFiles,
            AccessControlType.Deny);
        var security = dirInfo.GetAccessControl();
        security.AddAccessRule(deny);
        dirInfo.SetAccessControl(security);
        try
        {
            var result = installer.ApplyUpdate(newExe);

            Assert.False(result.Success);
            Assert.Equal("Update_NoWriteAccess", result.ErrorKey);
            // Честный отказ: ничего не тронуто, ничего не запущено
            Assert.Equal("OLD-EXE-BYTES", File.ReadAllText(_exePath));
            Assert.Equal("NEW-EXE-BYTES", File.ReadAllText(newExe));
            Assert.False(File.Exists(Path.ChangeExtension(_exePath, ".old")));
            Assert.Null(launcher.StartedPath);
            Assert.Null(launcher.ExitCode);
        }
        finally
        {
            // Вернуть права до Dispose фикстуры (иначе каталог не удалится)
            var cleanup = dirInfo.GetAccessControl();
            cleanup.RemoveAccessRule(deny);
            dirInfo.SetAccessControl(cleanup);
        }
    }

    [Fact]
    public void Apply_MissingTempFile_ApplyFailed_RolledBack()
    {
        File.WriteAllText(_exePath, "OLD-EXE-BYTES");
        var missing = Path.Combine(_root, "missing-new.exe");
        var (installer, launcher) = CreateApplier();

        var result = installer.ApplyUpdate(missing);

        Assert.False(result.Success);
        Assert.Equal("Update_ApplyFailed", result.ErrorKey);
        // Откат: exe вернулся на место, .old не остался, рестарта нет
        Assert.Equal("OLD-EXE-BYTES", File.ReadAllText(_exePath));
        Assert.False(File.Exists(Path.ChangeExtension(_exePath, ".old")));
        Assert.Null(launcher.StartedPath);
    }

    // ─── Очистка .old при старте ───

    [Fact]
    public void CleanupOld_RemovesLeftoverFile_KeepsCurrentExe()
    {
        var oldPath = Path.ChangeExtension(_exePath, ".old");
        File.WriteAllText(oldPath, "LEFTOVER");
        File.WriteAllText(_exePath, "CURRENT");

        UpdateInstaller.CleanupOldFile(_exePath);

        Assert.False(File.Exists(oldPath));
        Assert.Equal("CURRENT", File.ReadAllText(_exePath));
    }

    [Fact]
    public void CleanupOld_NothingToRemove_DoesNotThrow()
    {
        File.WriteAllText(_exePath, "CURRENT");

        Assert.Null(Record.Exception(() => UpdateInstaller.CleanupOldFile(_exePath)));
        // Пустой путь (Environment.ProcessPath недоступен) и отсутствующий каталог — молча
        Assert.Null(Record.Exception(() => UpdateInstaller.CleanupOldFile(string.Empty)));
        Assert.Null(Record.Exception(() => UpdateInstaller.CleanupOldFile(Path.Combine(_root, "no-such-app", "Puryx.exe"))));
    }

    // ─── Хелперы фикстуры ───

    /// <summary>Запрос на обновление с заданным URL ассета и хэшем (как из UpdateChecker).</summary>
    private static UpdateCheckResult UpdateWithSha(string assetUrl, string sha256) => new()
    {
        HasUpdate = true,
        CurrentVersion = "3.0.0",
        LatestVersion = "3.1.0",
        ReleaseUrl = "https://github.com/Harbuzilia/Puryx/releases/tag/v3.1.0",
        AssetUrl = assetUrl,
        Sha256 = sha256,
        NotesUrl = "https://github.com/Harbuzilia/Puryx/releases/tag/v3.1.0"
    };

    private UpdateInstaller CreateInstaller(HttpMessageHandler? handler = null) =>
        new(handler, _exePath, _tempDir, new RecordingLauncher());

    private (UpdateInstaller Installer, RecordingLauncher Launcher) CreateApplier()
    {
        var launcher = new RecordingLauncher();
        return (new UpdateInstaller(null, _exePath, _tempDir, launcher), launcher);
    }

    private static string Sha256Of(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));

    private static HttpResponseMessage Ok(byte[] payload)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentLength = payload.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static void TryDeleteTree(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Мок шва процессов: фиксирует запуск нового exe и код выхода.</summary>
    private sealed class RecordingLauncher : IUpdateProcessLauncher
    {
        public string? StartedPath { get; private set; }
        public int? ExitCode { get; private set; }

        public void StartNewProcess(string exePath) => StartedPath = exePath;
        public void Exit(int exitCode) => ExitCode = exitCode;
    }

    /// <summary>Собиратель отчётов прогресса (прямой IProgress, без синх-контекста).</summary>
    private sealed class ProgressCollector : IProgress<double>
    {
        private readonly object _gate = new();
        private readonly List<double> _values = [];

        public IReadOnlyList<double> Values
        {
            get
            {
                lock (_gate)
                {
                    return _values.ToList();
                }
            }
        }

        public void Report(double value)
        {
            lock (_gate)
            {
                _values.Add(value);
            }
        }
    }

    /// <summary>Стаб HttpMessageHandler: фиксирует URI/User-Agent запроса, отвечает заданным образом.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? UserAgent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(respond(request));
        }
    }
}
