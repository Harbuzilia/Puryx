using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SmartCleaner.Core.Update;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты UpdateChecker (ROADMAP v3.1, День 11, фаза U): контракт проверки
/// релизов GitHub на стабах HttpMessageHandler — шов конструктора, живая
/// сеть не используется. Сценарии: новая/та же/старая версия, защита от
/// draft/prerelease, битый JSON, тег без версии, отсутствие portable-ассета
/// по шаблону, отказ без SHA256 (хэш короче 64 hex не матчится), последний
/// SHA256-мачт из заметок, HTTP 403 → rate-limit, сетевой сбой и
/// тайм-аут-отмена → честные статусы, обязательные заголовки
/// (User-Agent/Accept) на эндпоинте /releases/latest, версия из сборки
/// при неявном currentVersion.
/// </summary>
public class UpdateCheckerTests
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/Harbuzilia/Puryx/releases/latest";

    private const string PortableAssetName = "Puryx-3.1.0-portable-win-x64.exe";
    private const string PortableAssetUrl =
        "https://github.com/Harbuzilia/Puryx/releases/download/v3.1.0/Puryx-3.1.0-portable-win-x64.exe";

    /// <summary>Два валидных 64-hex хэша: A — «старый», B — «последний» матч в заметках.</summary>
    private static readonly string ShaA = new('A', 64);
    private static readonly string ShaB = new('B', 64);

    [Fact]
    public async Task Check_NewerRelease_HasUpdateTrue_AllFieldsFilled()
    {
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"Release notes.\n\nSHA256: {ShaA}")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.True(result.HasUpdate);
        Assert.Null(result.ErrorKey);
        Assert.Equal("3.0.0", result.CurrentVersion);
        Assert.Equal("3.1.0", result.LatestVersion);
        Assert.Equal("https://github.com/Harbuzilia/Puryx/releases/tag/v3.1.0", result.ReleaseUrl);
        Assert.Equal(PortableAssetUrl, result.AssetUrl);
        Assert.Equal(ShaA, result.Sha256);
        // GitHub не даёт отдельного URL заметок — это страница релиза (html_url)
        Assert.Equal(result.ReleaseUrl, result.NotesUrl);
    }

    [Fact]
    public async Task Check_SameVersion_HasUpdateFalse_WithoutError()
    {
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.0.0", $"SHA256: {ShaA}",
            assetName: "Puryx-3.0.0-portable-win-x64.exe")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Null(result.ErrorKey);
        Assert.Equal("3.0.0", result.LatestVersion);
    }

    [Fact]
    public async Task Check_OlderReleaseVersion_HasUpdateFalse_NoDowngradeOffer()
    {
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v2.9.0", $"SHA256: {ShaA}",
            assetName: "Puryx-2.9.0-portable-win-x64.exe")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Null(result.ErrorKey);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public async Task Check_DraftOrPrereleaseFlag_Refused_InvalidResponse(string flag)
    {
        // /releases/latest такие релизы не отдаёт — защита от смены контракта API
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"SHA256: {ShaA}",
            draft: flag == "draft", prerelease: flag == "prerelease")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_InvalidResponse", result.ErrorKey);
    }

    [Fact]
    public async Task Check_MalformedJson_InvalidResponse()
    {
        var handler = new StubHandler(_ => Ok("{ this is not json "));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_InvalidResponse", result.ErrorKey);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("v3.x.9")]
    public async Task Check_TagWithoutParsableVersion_InvalidResponse(string tag)
    {
        var handler = new StubHandler(_ => Ok(BuildReleaseJson(tag, $"SHA256: {ShaA}",
            assetName: "Puryx-9.9.9-portable-win-x64.exe")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_InvalidResponse", result.ErrorKey);
    }

    [Fact]
    public async Task Check_NoPortableWinX64Asset_Refused_InvalidResponse()
    {
        // Ассет есть, но имя не соответствует шаблону Puryx-X.Y.Z-portable-win-x64.exe
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"SHA256: {ShaA}",
            assetName: "Puryx-3.1.0-setup-win-x64.exe")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_InvalidResponse", result.ErrorKey);
    }

    [Fact]
    public async Task Check_NotesWithoutSha256_Refused_NoSha256()
    {
        // Ассет без хэша → отказ: НЕ обновляемся без верификации
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", "Just some release notes, no checksum.")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_NoSha256", result.ErrorKey);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    [Fact]
    public async Task Check_Sha256ShorterThan64Hex_DoesNotMatch_Refused()
    {
        // «SHA256: <32 hex>» не матчится паттерном 64-hex — честный отказ, не тихое «нашли»
        var shortSha = new string('C', 32);
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"SHA256: {shortSha}")));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_NoSha256", result.ErrorKey);
    }

    [Fact]
    public async Task Check_MultipleSha256Lines_LastMatchWins()
    {
        // В заметках проекта SHA256 — последняя строка; парсер берёт ПОСЛЕДНИЙ матч
        var body = $"Old build:\nSHA256: {ShaA}\n\nRebuilt and re-uploaded:\nSHA256: {ShaB}";
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", body)));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.True(result.HasUpdate);
        Assert.Equal(ShaB, result.Sha256);
    }

    [Fact]
    public async Task Check_Http403_RateLimited_ExplicitStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_RateLimited", result.ErrorKey);
        Assert.Equal("3.0.0", result.CurrentVersion);
    }

    [Fact]
    public async Task Check_NetworkException_CheckFailedStatus_DoesNotThrow()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_CheckFailed", result.ErrorKey);
    }

    [Fact]
    public async Task Check_TimeoutLikeCancellation_CheckFailedStatus()
    {
        // HttpClient-тайм-аут: TaskCanceledException при НЕзапрошенной отмене — не краш
        var handler = new StubHandler(_ => throw new TaskCanceledException());
        var checker = new UpdateChecker(handler, "3.0.0");

        var result = await checker.CheckAsync();

        Assert.False(result.HasUpdate);
        Assert.Equal("Update_CheckFailed", result.ErrorKey);
    }

    [Fact]
    public async Task Check_SendsUserAgentAndAccept_ToLatestReleaseEndpoint()
    {
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"SHA256: {ShaA}")));
        var checker = new UpdateChecker(handler, "3.0.0");

        await checker.CheckAsync();

        Assert.Equal(LatestReleaseApiUrl, handler.RequestUri);
        Assert.Equal("Puryx-Updater/3.0.0", handler.UserAgent);
        Assert.Contains("application/vnd.github+json", handler.AcceptMediaTypes);
    }

    [Fact]
    public async Task Check_CurrentVersionNotProvided_ResolvedFromAssembly_IsThreePartVersion()
    {
        // Дефолт (currentVersion = null): версия процесса/сборки в форме X.Y.Z
        var handler = new StubHandler(_ => Ok(BuildReleaseJson("v3.1.0", $"SHA256: {ShaA}")));
        var checker = new UpdateChecker(handler);

        var result = await checker.CheckAsync();

        Assert.Matches(@"^\d+\.\d+\.\d+$", result.CurrentVersion);
        Assert.True(Version.TryParse(result.CurrentVersion, out _));
    }

    // ─── Хелперы фикстуры GitHub-ответа ───

    /// <summary>JSON ответа /releases/latest (компактно через JsonNode — без ручного экранирования).</summary>
    private static string BuildReleaseJson(
        string tag,
        string body,
        string assetName = PortableAssetName,
        bool draft = false,
        bool prerelease = false)
    {
        var root = new JsonObject
        {
            ["html_url"] = $"https://github.com/Harbuzilia/Puryx/releases/tag/{tag}",
            ["tag_name"] = tag,
            ["draft"] = draft,
            ["prerelease"] = prerelease,
            ["body"] = body,
            ["assets"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = assetName,
                    ["browser_download_url"] =
                        $"https://github.com/Harbuzilia/Puryx/releases/download/{tag}/{assetName}"
                }
            }
        };
        return root.ToJsonString();
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Стаб HttpMessageHandler: возвращает заданный ответ (или бросает исключение
    /// из фабрики); значения заголовков запроса копируются в момент отправки —
    /// до возможного Dispose сообщения.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? RequestUri { get; private set; }
        public string? UserAgent { get; private set; }
        public IReadOnlyList<string?> AcceptMediaTypes { get; private set; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            AcceptMediaTypes = request.Headers.Accept.Select(a => a.MediaType).ToList();
            return Task.FromResult(respond(request));
        }
    }
}
