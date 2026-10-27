namespace SmartCleaner.Core.Update;

/// <summary>
/// Результат проверки обновлений (ROADMAP v3.1, День 11, фаза U).
/// Успех: <see cref="HasUpdate"/> + поля релиза (<see cref="ErrorKey"/> null);
/// сбой: <see cref="ErrorKey"/> — ключ локализованного статуса из семейства
/// Update_* (CheckFailed/RateLimited/InvalidResponse/NoSha256) и заполненный
/// <see cref="CurrentVersion"/>, поля релиза пусты.
/// </summary>
public sealed record UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public string AssetUrl { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string NotesUrl { get; init; } = string.Empty;

    /// <summary>Ключ локализованного статуса при сбое; null при успехе.</summary>
    public string? ErrorKey { get; init; }
}

/// <summary>
/// Проверка обновлений по требованию (День 11): телеметрии нет — сеть
/// затрагивается только явным вызовом <see cref="CheckAsync"/>.
/// </summary>
public interface IUpdateChecker
{
    /// <summary>Проверить последний релиз GitHub; сбой — статус в результате, не исключение.</summary>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Заглушка RED-фазы TDD (День 11): контракт зафиксирован тестами
/// <c>UpdateCheckerTests</c>; реализация — GREEN-коммит. Параметры
/// конструктора (шов HttpMessageHandler + явная текущая версия) —
/// часть контракта.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    /// <param name="handler">Шов тестов; null — реальный сетевой стек.</param>
    /// <param name="currentVersion">Явная текущая версия (тесты); null — из сборки процесса.</param>
    public UpdateChecker(HttpMessageHandler? handler = null, string? currentVersion = null)
    {
    }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
