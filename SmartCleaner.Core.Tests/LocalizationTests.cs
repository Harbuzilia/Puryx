using System.Globalization;
using System.Reflection;
using SmartCleaner.Core.Localization;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты локализатора (ROADMAP v3.1, День 1, фаза L): инвариант паритета
/// ключей EN↔RU, непустые значения обеих культур, авто-детект культуры через
/// шов-параметр, читаемый fallback на отсутствующий ключ и соответствие
/// статических свойств Localizer ключам resx (безопасность {x:Static} в XAML:
/// имя свойства обязано существовать в обеих культурах, иначе XAML-загрузка
/// молча покажет имя ключа).
/// </summary>
public class LocalizationTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public void Dispose()
    {
        // Статическое состояние не должно протекать в другие тесты
        CultureInfo.CurrentUICulture = _originalUiCulture;
        Localizer.Culture = _originalLocalizerCulture;
    }

    [Fact]
    public void Keys_Parity_EnglishNeutral_And_Russian()
    {
        var neutralKeys = Localizer.GetKeys(CultureInfo.InvariantCulture).OrderBy(k => k).ToList();
        var ruKeys = Localizer.GetKeys(new CultureInfo("ru")).OrderBy(k => k).ToList();

        Assert.NotEmpty(neutralKeys);
        Assert.Equal(neutralKeys, ruKeys);
    }

    [Fact]
    public void GetString_ReturnsNonEmpty_ForAllKeys_InBothCultures()
    {
        var keys = Localizer.GetKeys(CultureInfo.InvariantCulture);

        foreach (var culture in new[] { CultureInfo.InvariantCulture, new CultureInfo("ru") })
        {
            Localizer.Culture = culture;
            foreach (var key in keys)
            {
                var value = Localizer.GetString(key);
                Assert.False(string.IsNullOrWhiteSpace(value),
                    $"Пустое значение ключа '{key}' для культуры '{culture.Name}'");
            }
        }
    }

    [Fact]
    public void GetString_RussianCulture_ReturnsText_DifferentFromEnglish()
    {
        Localizer.Culture = new CultureInfo("ru");
        var ru = Localizer.GetString("MainWindow_Title");

        Localizer.Culture = CultureInfo.InvariantCulture;
        var en = Localizer.GetString("MainWindow_Title");

        Assert.NotEqual(ru, en);
    }

    [Theory]
    [InlineData("auto", "ru-RU", "ru")]
    [InlineData("Auto", "ru", "ru")]
    [InlineData(null, "ru-RU", "ru")]
    [InlineData("", "ru", "ru")]
    [InlineData("auto", "en-US", "")]
    [InlineData("auto", "fr-FR", "")]
    [InlineData("auto", "", "")]
    [InlineData(null, null, "")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("en", "ru-RU", "")]
    [InlineData("de", "ru-RU", "")]
    [InlineData("ru", null, "ru")]
    public void ResolveCulture_SettingWinsOverAutoDetect(string? setting, string? uiCulture, string expectedName)
    {
        var resolved = Localizer.ResolveCulture(setting, uiCulture);

        Assert.Equal(expectedName, resolved.Name);
    }

    [Fact]
    public void Init_AutoSetting_UsesCurrentUICulture()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
            Localizer.Init("Auto");
            Assert.Equal("ru", Localizer.Culture?.Name);

            CultureInfo.CurrentUICulture = new CultureInfo("en-US");
            Localizer.Init(null);
            Assert.Equal(string.Empty, Localizer.Culture?.Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void GetString_MissingKey_ReturnsKeyName_AsReadableFallback()
    {
        const string missingKey = "No_Such_Key_Abc123";

        Localizer.Culture = new CultureInfo("ru");
        var ru = Localizer.GetString(missingKey);
        Assert.Equal(missingKey, ru);

        Localizer.Culture = CultureInfo.InvariantCulture;
        var en = Localizer.GetString(missingKey);
        Assert.Equal(missingKey, en);
    }

    [Fact]
    public void Localizer_StaticProperties_MatchResxKeys()
    {
        var propertyNames = typeof(Localizer)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        var keys = Localizer.GetKeys(CultureInfo.InvariantCulture).OrderBy(k => k).ToList();

        Assert.NotEmpty(propertyNames);
        Assert.Equal(keys, propertyNames);
    }
}
