using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Инвариант безопасности cookie-категории (ROADMAP v3.1, День 14, фаза B):
/// cookie-хранилища браузеров — <see cref="RiskCategory.UserData"/> и попадают
/// в выдачу ТОЛЬКО при явном opt-in (<see cref="ScanSettingsConfig.BrowserCleanCookies"/>).
/// При выключенной опции их нет в результате ВООБЩЕ (не «не выбраны»), при
/// включённой — всегда <c>IsSelected = false</c> и <see cref="ScannedItem.IsUserDataOptIn"/>.
///
/// Фикстура — мок-структура каталогов браузеров через тест-шов корней
/// %LOCALAPPDATA%/%APPDATA% (как сканер-тесты подставляют корень сканирования):
/// Chromium: <c>Default\Network\Cookies</c> (актуальный путь) + legacy
/// <c>Cookies</c> в корне профиля (устаревший) + второй профиль <c>Profile 1</c>;
/// Firefox: <c>Profiles\&lt;profile&gt;\cookies.sqlite</c>.
/// </summary>
public class BrowserScannerCookieTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"puryx-browser-{Guid.NewGuid():N}");

    private readonly string _localAppData;
    private readonly string _appData;

    public BrowserScannerCookieTests()
    {
        _localAppData = Path.Combine(_root, "LocalAppData");
        _appData = Path.Combine(_root, "AppData");

        // Chrome: Default с актуальным Network\Cookies + legacy Cookies (оба);
        // Profile 1 — только legacy Cookies (фолбэк для старых профилей).
        WriteFile(Path.Combine(_localAppData, "Google", "Chrome", "User Data", "Default", "Network", "Cookies"), 4096);
        WriteFile(Path.Combine(_localAppData, "Google", "Chrome", "User Data", "Default", "Cookies"), 2048);
        WriteFile(Path.Combine(_localAppData, "Google", "Chrome", "User Data", "Profile 1", "Cookies"), 1024);

        // Firefox
        WriteFile(Path.Combine(_appData, "Mozilla", "Firefox", "Profiles", "abc123.default", "cookies.sqlite"), 8192);
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

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private BrowserScanner CreateScanner(bool cookieOptIn)
    {
        var config = new InMemoryConfigService([])
        {
            ScanSettings = new ScanSettingsConfig { BrowserCleanCookies = cookieOptIn }
        };
        return new BrowserScanner(new AllowAllSafetyService(), config, _localAppData, _appData);
    }

    private static List<ScannedItem> CookieItems(IEnumerable<ScannedItem> items) =>
        items.Where(i =>
                i.IsUserDataOptIn ||
                i.Path.EndsWith("Cookies", StringComparison.OrdinalIgnoreCase) ||
                i.Path.EndsWith("cookies.sqlite", StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// ГЛАВНЫЙ ИНВАРИАНТ: без явного opt-in cookie-элементов в результате нет вообще.
    /// </summary>
    [Fact]
    public async Task ScanAsync_CookiesOptInOff_ReturnsNoCookieItemsAtAll()
    {
        var scanner = CreateScanner(cookieOptIn: false);

        var result = await scanner.ScanAsync();

        Assert.Empty(CookieItems(result.Items));
        Assert.DoesNotContain(result.Items, i => i.Risk == RiskCategory.UserData);
    }

    /// <summary>
    /// Дефолт единой настройки — opt-in выключен (свежий конфиг):
    /// безопасность не зависит от того, что кто-то забыл записать файл.
    /// </summary>
    [Fact]
    public async Task ScanAsync_DefaultConfig_KeepsCookiesHidden()
    {
        var config = new InMemoryConfigService([]); // ScanSettings == null → new ScanSettingsConfig()
        var scanner = new BrowserScanner(new AllowAllSafetyService(), config, _localAppData, _appData);

        var result = await scanner.ScanAsync();

        Assert.Empty(CookieItems(result.Items));
    }

    /// <summary>
    /// Opt-in включён: Chromium и Firefox cookie-хранилища выдаются как UserData,
    /// всегда невыбранные, с локализованным описанием и честным «разлогинит».
    /// </summary>
    [Fact]
    public async Task ScanAsync_CookiesOptInOn_ReturnsUserDataItems_AlwaysUnselected()
    {
        var scanner = CreateScanner(cookieOptIn: true);

        var result = await scanner.ScanAsync();

        var cookies = CookieItems(result.Items);
        Assert.Equal(3, cookies.Count);

        // все — UserData, opt-in-помечены, не выбраны, файлы (не каталоги)
        Assert.All(cookies, item =>
        {
            Assert.Equal(RiskCategory.UserData, item.Risk);
            Assert.True(item.IsUserDataOptIn);
            Assert.False(item.IsSelected);
            Assert.False(item.IsDirectory);
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.False(string.IsNullOrWhiteSpace(item.Explanation));
            Assert.False(string.IsNullOrWhiteSpace(item.HowToRestore));
        });

        // Chromium: актуальный Network\Cookies приоритетнее legacy Cookies
        var chrome = Assert.Single(cookies, i => i.ParentApp == "Chrome"
            && i.Path.Contains(Path.Combine("Default"), StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith(Path.Combine("Network", "Cookies"), chrome.Path, StringComparison.OrdinalIgnoreCase);

        // Chromium: профиль без Network\Cookies — фолбэк на legacy Cookies в корне профиля
        var legacy = Assert.Single(cookies, i => i.Path.Contains("Profile 1", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("Cookies", legacy.Path, StringComparison.OrdinalIgnoreCase);

        // Firefox: cookies.sqlite
        var firefox = Assert.Single(cookies, i => i.ParentApp == "Firefox");
        Assert.EndsWith("cookies.sqlite", firefox.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Opt-in не появляется сам: при перечитывании конфига значение сохраняется
    /// как записано (persistence единой настройки scan_settings.json).
    /// </summary>
    [Fact]
    public void BrowserCleanCookies_DefaultsOff_AndRoundTripsThroughConfigDirectory()
    {
        Assert.False(new ScanSettingsConfig().BrowserCleanCookies);

        var configDirectory = Path.Combine(_root, "config");
        var config = new FileBackedConfigService(configDirectory);

        config.Save(ScanSettingsConfig.Filename, new ScanSettingsConfig
        {
            MftScanMode = "On",
            BrowserCleanCookies = true
        });

        var loaded = config.Load<ScanSettingsConfig>(ScanSettingsConfig.Filename);

        Assert.True(loaded.BrowserCleanCookies);
        Assert.Equal("On", loaded.MftScanMode); // соседняя опция не теряется
    }
}