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
    public Task<UpdateDownloadResult> DownloadAsync(
        UpdateCheckResult update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public bool VerifySha256(string tempFilePath, string expectedSha256)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public UpdateApplyResult ApplyUpdate(string tempFilePath)
        => throw new NotImplementedException();

    /// <summary>
    /// Очистка артефакта прошлого self-update: удалить &lt;exe&gt;.old, если
    /// существует. Вызывается при старте приложения; исключения глотаются —
    /// очистка необязательна для запуска.
    /// </summary>
    public static void CleanupOldFile(string currentExePath)
        => throw new NotImplementedException();

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
