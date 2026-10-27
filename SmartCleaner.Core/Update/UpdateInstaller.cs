using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace SmartCleaner.Core.Update;

/// <summary>
/// Результат скачивания обновления (ROADMAP v3.1, День 12, фаза A).
/// Успех: <see cref="TempFilePath"/> заполнен, <see cref="ErrorKey"/> null;
/// сбой: ключ локализованного статуса семейства Update_*
/// (NotHttps/DownloadFailed) и пустой путь.
/// </summary>
public sealed record UpdateDownloadResult
{
    public string TempFilePath { get; init; } = string.Empty;

    /// <summary>Ключ локализованного статуса при сбое; null при успехе.</summary>
    public string? ErrorKey { get; init; }

    /// <summary>Успех = путь есть и нет ключа ошибки.</summary>
    public bool Success => ErrorKey is null && TempFilePath.Length > 0;

    public static UpdateDownloadResult Ok(string tempFilePath) => new() { TempFilePath = tempFilePath };

    public static UpdateDownloadResult Fail(string errorKey) => new() { ErrorKey = errorKey };
}

/// <summary>
/// Результат применения обновления: успех — процесс уже перезапущен
/// (<see cref="IUpdateProcessLauncher"/>), текущий exe продолжается только
/// при отказе с ключом семейства Update_* (NoWriteAccess/ApplyFailed).
/// </summary>
public sealed record UpdateApplyResult
{
    public bool Success { get; init; }

    /// <summary>Ключ локализованного статуса при отказе; null при успехе.</summary>
    public string? ErrorKey { get; init; }

    public static UpdateApplyResult Ok() => new() { Success = true };

    public static UpdateApplyResult Fail(string errorKey) => new() { Success = false, ErrorKey = errorKey };
}

/// <summary>
/// Шов процессов (День 12): запуск нового exe после swap и завершение
/// текущего. Тесты подменяют моком — реальный Process.Start/Environment.Exit
/// не выполняются; приложение получает реальную реализацию через DI.
/// </summary>
public interface IUpdateProcessLauncher
{
    /// <summary>Запустить обновлённый exe (после rename-swap).</summary>
    void StartNewProcess(string exePath);

    /// <summary>Завершить текущий процесс с кодом выхода.</summary>
    void Exit(int exitCode);
}

/// <summary>Реальный запуск/выход: Process.Start нового exe и Environment.Exit текущего.</summary>
public sealed class UpdateProcessLauncher : IUpdateProcessLauncher
{
    /// <inheritdoc />
    public void StartNewProcess(string exePath) =>
        Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

    /// <inheritdoc />
    public void Exit(int exitCode) => Environment.Exit(exitCode);
}

/// <summary>
/// Установка обновления (ROADMAP v3.1, День 12, фаза A): скачивание ассета
/// в temp с прогрессом, потоковая верификация SHA256 и rename-swap с
/// рестартом. Portable и installed — общий код, различие только в правах
/// на каталог программы (нет прав → честный отказ Update_NoWriteAccess,
/// БЕЗ элевации — новых elevated-векторов не создаём).
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Скачать ассет релиза в temp-файл (стримингом, только HTTPS).
    /// Отмена вызывающим кодом пробрасывается; сбой — честный ключ
    /// Update_* в результате, а не исключение.
    /// </summary>
    Task<UpdateDownloadResult> DownloadAsync(
        UpdateCheckResult update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверить SHA256 temp-файла потоково (файл не грузится в память).
    /// true — хэш совпал; false — мисматч (temp удаляется) или файл
    /// недоступен: вызывающий отказывает с Update_HashMismatch.
    /// </summary>
    bool VerifySha256(string tempFilePath, string expectedSha256);

    /// <summary>
    /// Применить обновление rename-swap: текущий exe → &lt;имя&gt;.old рядом,
    /// новый на место текущего, запуск нового, Exit(0) текущего.
    /// Сбой переноса — откат exe на место; нет прав записи в каталог —
    /// честный отказ Update_NoWriteAccess.
    /// </summary>
    UpdateApplyResult ApplyUpdate(string tempFilePath);
}

/// <summary>
/// Инсталлер обновлений (День 12): HttpClient-шов в конструкторе (как
/// UpdateChecker — тесты на стабах HttpMessageHandler без живой сети),
/// явные currentExePath/tempDirectory для фантомных файлов тестов и
/// шов процессов. Тайм-аут HttpClient отключён: 70 МБ ассет не вписывается
/// в 15-секундный лимит проверки — отмена только через CancellationToken.
/// </summary>
public sealed class UpdateInstaller : IUpdateInstaller, IDisposable
{
    /// <summary>Буфер стриминга скачивания (80 КБ — дефолт Stream-копирования).</summary>
    private const int StreamBufferSize = 81920;

    private readonly HttpClient _httpClient;
    private readonly string _currentExePath;
    private readonly string _tempDirectory;
    private readonly IUpdateProcessLauncher _processLauncher;

    /// <param name="handler">Шов тестов; null — реальный сетевой стек.</param>
    /// <param name="currentExePath">Путь текущего exe (фантомы тестов); null — процесс/сборка.</param>
    /// <param name="tempDirectory">Каталог temp-скачивания (тесты); null — Path.GetTempPath().</param>
    /// <param name="processLauncher">Шов процессов (мок тестов); null — реальный launcher.</param>
    public UpdateInstaller(
        HttpMessageHandler? handler = null,
        string? currentExePath = null,
        string? tempDirectory = null,
        IUpdateProcessLauncher? processLauncher = null)
    {
        _httpClient = new HttpClient(handler ?? new HttpClientHandler())
        {
            // Тайм-аут отключён осознанно: медленный канал не должен обрывать
            // скачивание 70 МБ; прерывание — только отмена (CancellationToken)
            Timeout = Timeout.InfiniteTimeSpan
        };
        var version = (Assembly.GetEntryAssembly() ?? typeof(UpdateInstaller).Assembly)
            .GetName().Version ?? new Version(0, 0, 0);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Puryx-Updater/{version}");

        _currentExePath = currentExePath
            ?? Environment.ProcessPath
            ?? typeof(UpdateInstaller).Assembly.Location;
        _tempDirectory = tempDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(_tempDirectory);
        _processLauncher = processLauncher ?? new UpdateProcessLauncher();
    }

    /// <inheritdoc />
    public async Task<UpdateDownloadResult> DownloadAsync(
        UpdateCheckResult update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        // Только HTTPS: транспорт обновления не понижается, отказ — ДО любого сетевого запроса
        if (!Uri.TryCreate(update.AssetUrl, UriKind.Absolute, out var assetUri) ||
            !string.Equals(assetUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateDownloadResult.Fail("Update_NotHttps");
        }

        var tempPath = Path.Combine(_tempDirectory, $"Puryx-update-{update.LatestVersion}.exe");
        try
        {
            using var response = await _httpClient
                .GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                TryDeleteFile(tempPath);
                return UpdateDownloadResult.Fail("Update_DownloadFailed");
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var source = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None, StreamBufferSize);

            var buffer = new byte[StreamBufferSize];
            long copiedBytes = 0;
            int chunk;
            while ((chunk = await source
                       .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, chunk), cancellationToken).ConfigureAwait(false);
                copiedBytes += chunk;
                if (totalBytes > 0)
                {
                    progress?.Report((double)copiedBytes / totalBytes);
                }
            }

            // Финальный отчёт и при неизвестном Content-Length — индикатор завершения
            progress?.Report(1.0);
            return UpdateDownloadResult.Ok(tempPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена инициирована вызывающим кодом — пробрасываем честно (паттерн UpdateChecker),
            // partial-файл в temp вычищаем
            TryDeleteFile(tempPath);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            return UpdateDownloadResult.Fail("Update_DownloadFailed");
        }
        catch (HttpRequestException)
        {
            TryDeleteFile(tempPath);
            return UpdateDownloadResult.Fail("Update_DownloadFailed");
        }
        catch (IOException)
        {
            // Недоступный temp-каталог/диск — тот же честный отказ скачивания
            TryDeleteFile(tempPath);
            return UpdateDownloadResult.Fail("Update_DownloadFailed");
        }
    }

    /// <inheritdoc />
    public bool VerifySha256(string tempFilePath, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256) || !File.Exists(tempFilePath))
        {
            return false;
        }

        string actualHex;
        try
        {
            // SHA256.HashData(Stream) хэширует поток чанками — 70 МБ ассет не грузится в память
            using var stream = File.OpenRead(tempFilePath);
            actualHex = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            // Нечитаемый файл — верификация не удалась, refusal вызывающего
            return false;
        }

        if (string.Equals(actualHex, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Мисматч: невалидная загрузка не хранится в temp; вызывающий отказывает с Update_HashMismatch
        TryDeleteFile(tempFilePath);
        return false;
    }

    /// <inheritdoc />
    public UpdateApplyResult ApplyUpdate(string tempFilePath)
    {
        var programDirectory = Path.GetDirectoryName(_currentExePath);
        if (string.IsNullOrEmpty(programDirectory))
        {
            return UpdateApplyResult.Fail("Update_ApplyFailed");
        }

        // Права записи в каталог программы (Program Files без админа): probe-файл —
        // отказ честный, с подсказкой, БЕЗ элевации (новых elevated-векторов нет)
        try
        {
            var probePath = Path.Combine(programDirectory, Path.GetRandomFileName());
            using (File.Create(probePath))
            {
            }
            File.Delete(probePath);
        }
        catch (UnauthorizedAccessException)
        {
            return UpdateApplyResult.Fail("Update_NoWriteAccess");
        }
        catch (IOException)
        {
            return UpdateApplyResult.Fail("Update_ApplyFailed");
        }

        var oldPath = Path.ChangeExtension(_currentExePath, ".old");

        // Шаг 1: текущий exe → <имя>.old рядом; переживший .old от прошлой попытки замещается
        try
        {
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
            File.Move(_currentExePath, oldPath);
        }
        catch (UnauthorizedAccessException)
        {
            return UpdateApplyResult.Fail("Update_NoWriteAccess");
        }
        catch (IOException)
        {
            return UpdateApplyResult.Fail("Update_ApplyFailed");
        }

        // Шаг 2: новый exe на место текущего; сбой — откат текущего из .old
        try
        {
            File.Move(tempFilePath, _currentExePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryMoveBack(oldPath, _currentExePath);
            return UpdateApplyResult.Fail(
                ex is UnauthorizedAccessException ? "Update_NoWriteAccess" : "Update_ApplyFailed");
        }

        // Swap завершён: новый exe на месте, старый ждёт CleanupOldFile при следующем старте
        _processLauncher.StartNewProcess(_currentExePath);
        _processLauncher.Exit(0);
        return UpdateApplyResult.Ok();
    }

    /// <summary>
    /// Очистка артефакта прошлого self-update: удалить &lt;exe&gt;.old, если
    /// существует. Вызывается при старте приложения; исключения глотаются —
    /// очистка необязательна для запуска.
    /// </summary>
    public static void CleanupOldFile(string currentExePath)
    {
        if (string.IsNullOrWhiteSpace(currentExePath))
        {
            return;
        }

        try
        {
            var oldPath = Path.ChangeExtension(currentExePath, ".old");
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Молча: запуск приложения не должен зависеть от уборки
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    /// <summary>Лучшее усилие удаления — мусор в temp не должен валить честный отказ.</summary>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Откат swap: вернуть старый exe из .old на место (лучшее усилие, молча).</summary>
    private static void TryMoveBack(string fromPath, string toPath)
    {
        try
        {
            if (File.Exists(fromPath))
            {
                File.Move(fromPath, toPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Откат не удался: .old остаётся рядом — восстановление вручную; отказ ниже честный
        }
    }
}
