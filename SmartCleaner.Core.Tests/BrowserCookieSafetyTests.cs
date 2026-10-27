using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Защита cookie-элементов обычными механизмами SafetyService (ROADMAP v3.1,
/// День 14, фаза B): whitelist-паттерны применяются к путям cookie-файлов как
/// к любым другим — включённый opt-in не открывает обход.
/// Единственное ослабление — защищённый период (cookie-БД пишется браузером
/// постоянно, иначе категория нефункциональна); оно узкое: только
/// <see cref="ScannedItem.IsUserDataOptIn"/>, whitelist и per-app защита остаются.
/// </summary>
public class BrowserCookieSafetyTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"puryx-cookie-{Guid.NewGuid():N}");

    private readonly string _cookiePath;

    public BrowserCookieSafetyTests()
    {
        _cookiePath = Path.Combine(_root, "Network", "Cookies");
        Directory.CreateDirectory(Path.GetDirectoryName(_cookiePath)!);
        File.WriteAllBytes(_cookiePath, new byte[512]); // свежий файл — внутри защищённого периода
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Уборка тестового каталога не влияет на результат
        }
    }

    private SafetyService CreateSafety()
    {
        var config = new InMemoryConfigService([]);
        return new SafetyService(config, new KnowledgeBase(config));
    }

    private ScannedItem CookieItem(bool optIn = true) => new()
    {
        Path = _cookiePath,
        Size = 512,
        Risk = RiskCategory.UserData,
        IsUserDataOptIn = optIn,
        IsDirectory = false
    };

    /// <summary>
    /// Whitelist-паттерн `**\Network\Cookies` блокирует удаление cookie-файла
    /// (пользователь защитил конкретный браузер от чистки).
    /// </summary>
    [Fact]
    public void ValidateForDeletion_WhitelistPatternForCookiePath_Blocks()
    {
        var safety = CreateSafety();
        safety.AddToWhitelist(@"**\Network\Cookies");

        var validation = safety.ValidateForDeletion(CookieItem());

        Assert.False(validation.CanDelete);
        Assert.False(string.IsNullOrWhiteSpace(validation.BlockReason));
    }

    /// <summary>
    /// Без whitelist opt-in cookie-элемент проходит: свежий файл не упирается
    /// в защищённый период (иначе категория нерабочая — браузер пишет cookie постоянно).
    /// </summary>
    [Fact]
    public void ValidateForDeletion_OptInCookie_ExemptFromProtectedPeriod()
    {
        var safety = CreateSafety();
        safety.ProtectedPeriod = TimeSpan.FromHours(24); // файл только что создан — «внутри»

        var validation = safety.ValidateForDeletion(CookieItem());

        Assert.True(validation.CanDelete);
    }

    /// <summary>
    /// Ослабление узкое: обычные пользовательские данные, изменённые только что,
    /// по-прежнему блокируются защищённым периодом.
    /// </summary>
    [Fact]
    public void ValidateForDeletion_UserDataWithoutOptIn_StillBlockedByProtectedPeriod()
    {
        var safety = CreateSafety();
        safety.ProtectedPeriod = TimeSpan.FromHours(24);

        var validation = safety.ValidateForDeletion(CookieItem(optIn: false));

        Assert.False(validation.CanDelete);
        Assert.False(string.IsNullOrWhiteSpace(validation.BlockReason));
    }
}