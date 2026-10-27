using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

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

    /// <summary>Сбой с ключом локализованного статуса; CurrentVersion сохраняется для UI.</summary>
    public static UpdateCheckResult Failure(string currentVersion, string errorKey) => new()
    {
        HasUpdate = false,
        CurrentVersion = currentVersion,
        ErrorKey = errorKey
    };
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
/// Проверка обновлений через GitHub Releases (ROADMAP v3.1, День 11, фаза U).
/// GET /repos/Harbuzilia/Puryx/releases/latest — эндпоинт не возвращает
/// draft/prerelease-релизы; Accept application/vnd.github+json; User-Agent
/// обязателен (требование GitHub API) — Puryx-Updater/&lt;version&gt;; тайм-аут 15 с.
/// Сравнение версий: v-префикс тега срезается, Version-парсинг, сравнение по
/// major.minor.build; невалидный тег — честная ошибка (Update_InvalidResponse).
/// Ассет — имя по шаблону Puryx-X.Y.Z-portable-win-x64.exe. SHA256 — последний
/// матч «SHA256: &lt;64 hex&gt;» в body заметок (в заметках проекта хэш — последняя
/// строка); ассет без валидного хэша → отказ (Update_NoSha256): не обновляемся
/// без возможности верификации (проверка хэша — День 12).
/// Сбой сети/тайм-аут/не-200 → Update_CheckFailed; HTTP 403 (лимит запросов
/// GitHub) → Update_RateLimited. Шов тестов: конструктор с HttpMessageHandler
/// и явной текущей версией.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker, IDisposable
{
    /// <summary>Эндпоинт последнего стабильного релиза.</summary>
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Harbuzilia/Puryx/releases/latest";

    /// <summary>Шаблон имени portable-ассета: Puryx-X.Y.Z-portable-win-x64.exe.</summary>
    private const string PortableAssetNameTemplate = "Puryx-{0}-portable-win-x64.exe";

    /// <summary>Тайм-аут запроса: ручная проверка не должна подвешивать UI.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>SHA256 в заметках: строка «SHA256: &lt;64 hex&gt;»; берётся ПОСЛЕДНИЙ матч.</summary>
    private static readonly Regex Sha256Pattern =
        new(@"SHA256[:\s]+([0-9A-Fa-f]{64})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly HttpClient _httpClient;
    private readonly Version _current;
    private readonly string _currentVersion;

    /// <param name="handler">Шов тестов; null — реальный сетевой стек.</param>
    /// <param name="currentVersion">Явная текущая версия (тесты); null — версия процесса/сборки.</param>
    public UpdateChecker(HttpMessageHandler? handler = null, string? currentVersion = null)
    {
        _current = NormalizeVersion(ParseCurrentVersion(currentVersion));
        _currentVersion = _current.ToString();
        _httpClient = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = RequestTimeout };
        // GitHub API требует User-Agent; UA идентифицирует апдейтер и его версию
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"Puryx-Updater/{_currentVersion}");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient
                .GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                // GitHub отвечает 403 при исчерпании лимита неаутентифицированных запросов
                return UpdateCheckResult.Failure(_currentVersion, "Update_RateLimited");
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failure(_currentVersion, "Update_CheckFailed");
            }

            var json = await response.Content
                .ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            var status = TryParseRelease(json, _current, _currentVersion, out var result);
            return status switch
            {
                ReleaseParseStatus.Success => result,
                ReleaseParseStatus.NoSha256 => UpdateCheckResult.Failure(_currentVersion, "Update_NoSha256"),
                _ => UpdateCheckResult.Failure(_currentVersion, "Update_InvalidResponse")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена инициирована вызывающим кодом — пробрасываем честно
            throw;
        }
        catch (OperationCanceledException)
        {
            // HttpClient-тайм-аут (15 с): TaskCanceledException без запроса отмены
            return UpdateCheckResult.Failure(_currentVersion, "Update_CheckFailed");
        }
        catch (HttpRequestException)
        {
            return UpdateCheckResult.Failure(_currentVersion, "Update_CheckFailed");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();

    /// <summary>Классификация разбора ответа /releases/latest.</summary>
    private enum ReleaseParseStatus
    {
        Success,
        InvalidResponse,
        NoSha256
    }

    /// <summary>
    /// Разбор JSON релиза в результат. Неверные структуры — false с Empty-результатом;
    /// Success — заполненный результат. Вызывающий код превращает статус в ErrorKey.
    /// </summary>
    private static ReleaseParseStatus TryParseRelease(
        string json, Version current, string currentVersion, out UpdateCheckResult result)
    {
        result = UpdateCheckResult.Failure(currentVersion, "Update_InvalidResponse");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            // /releases/latest не возвращает draft/prerelease — контракт защищаем явно
            if (IsTrue(root, "draft") || IsTrue(root, "prerelease"))
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            if (!root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String)
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            var tag = tagElement.GetString()?.Trim();
            if (string.IsNullOrEmpty(tag))
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            // v-префикс срезается; дальше обязан парситься как Version — иначе честная ошибка
            var versionText = tag[0] is 'v' or 'V' ? tag[1..] : tag;
            if (!Version.TryParse(versionText, out var latestRaw))
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            var latest = NormalizeVersion(latestRaw);
            var latestVersion = latest.ToString();

            if (!root.TryGetProperty("html_url", out var urlElement) ||
                urlElement.ValueKind != JsonValueKind.String)
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            var releaseUrl = urlElement.GetString();
            if (string.IsNullOrEmpty(releaseUrl))
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            var body = root.TryGetProperty("body", out var bodyElement) &&
                       bodyElement.ValueKind == JsonValueKind.String
                ? bodyElement.GetString() ?? string.Empty
                : string.Empty;

            // Portable-ассет win-x64: имя обязано соответствовать шаблону под версию релиза
            var expectedAssetName = string.Format(
                CultureInfo.InvariantCulture, PortableAssetNameTemplate, latestVersion);
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assetsElement) &&
                assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    if (!asset.TryGetProperty("name", out var nameElement) ||
                        nameElement.ValueKind != JsonValueKind.String ||
                        !string.Equals(nameElement.GetString(), expectedAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (asset.TryGetProperty("browser_download_url", out var downloadElement) &&
                        downloadElement.ValueKind == JsonValueKind.String)
                    {
                        assetUrl = downloadElement.GetString();
                    }

                    break;
                }
            }

            if (string.IsNullOrEmpty(assetUrl))
            {
                return ReleaseParseStatus.InvalidResponse;
            }

            // Хэш обязателен: без SHA256 обновление не предлагается (верификация Дня 12)
            var sha256 = FindSha256(body);
            if (sha256 is null)
            {
                return ReleaseParseStatus.NoSha256;
            }

            result = new UpdateCheckResult
            {
                HasUpdate = latest.CompareTo(current) > 0,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                ReleaseUrl = releaseUrl,
                AssetUrl = assetUrl,
                Sha256 = sha256,
                // Отдельного URL заметок GitHub не даёт — это страница релиза (html_url)
                NotesUrl = releaseUrl
            };
            return ReleaseParseStatus.Success;
        }
        catch (JsonException)
        {
            return ReleaseParseStatus.InvalidResponse;
        }
    }

    /// <summary>Последний матч SHA256-паттерна в тексте заметок; null — валидного хэша нет.</summary>
    private static string? FindSha256(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var matches = Sha256Pattern.Matches(body);
        return matches.Count > 0 ? matches[^1].Groups[1].Value : null;
    }

    /// <summary>Явное true у булева свойства (отсутствие/другой тип — не считается).</summary>
    private static bool IsTrue(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>Текущая версия: явная (тесты) или процесса (GetEntryAssembly → App), fallback — сборка Core.</summary>
    private static Version ParseCurrentVersion(string? currentVersion)
    {
        if (currentVersion is null)
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateChecker).Assembly;
            // AssemblyName.Version практических сборок не null; 0.0.0 — безопасный fallback
            return assembly.GetName().Version ?? new Version(0, 0, 0);
        }

        if (!Version.TryParse(currentVersion.Trim(), out var parsed))
        {
            throw new ArgumentException(
                $"Не удалось разобрать текущую версию: '{currentVersion}'", nameof(currentVersion));
        }

        return parsed;
    }

    /// <summary>Приведение к major.minor.build: релизные теги проекта трёхкомпонентные.</summary>
    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);
}
