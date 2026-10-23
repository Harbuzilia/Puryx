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
    public void Localizer_StaticProperties_AreSubsetOfResxKeys()
    {
        var propertyNames = typeof(Localizer)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        var keys = Localizer.GetKeys(CultureInfo.InvariantCulture).OrderBy(k => k).ToList();

        // Инвариант (День 4): свойство обязано иметь ключ в resx — иначе XAML
        // {x:Static} покажет имя ключа. Обратное не требуется: VM-строки
        // (День 4, GetString из кода) ключи без XAML-свойств.
        var orphans = propertyNames.Except(keys).ToList();

        Assert.NotEmpty(propertyNames);
        Assert.Empty(orphans);
    }

    /// <summary>
    /// День 4: GetString(key, args) подставляет {0}/{1} по значению resx
    /// текущей культуры — в обеих культурах свой текст с теми же аргументами.
    /// </summary>
    [Fact]
    public void GetString_WithFormatArgs_SubstitutesPlaceholders_InBothCultures()
    {
        Localizer.Culture = CultureInfo.InvariantCulture;
        var en = Localizer.GetString("Main_Status_Found", "1.2 GB", 42);
        Assert.Equal("Found: 1.2 GB in 42 items", en);

        Localizer.Culture = new CultureInfo("ru");
        var ru = Localizer.GetString("Main_Status_Found", "1,2 ГБ", 42);
        Assert.Equal("Найдено: 1,2 ГБ в 42 элементах", ru);
    }

    /// <summary>
    /// День 4: числовые и временные аргументы форматируются по ТЕКУЩЕЙ культуре
    /// Localizer (ru: «1,5» и «23.10.2026»), а не инвариантно.
    /// </summary>
    [Fact]
    public void GetString_WithFormatArgs_FormatsNumbersAndDates_ByCurrentCulture()
    {
        var number = 1.5;
        var date = new DateTime(2026, 10, 23);

        Localizer.Culture = CultureInfo.InvariantCulture;
        var en = Localizer.GetString("Main_Status_Found", number, date);
        Assert.Contains("1.5", en);
        Assert.Contains("10/23/2026", en);

        Localizer.Culture = new CultureInfo("ru");
        var ru = Localizer.GetString("Main_Status_Found", number, date);
        Assert.Contains("1,5", ru);
        Assert.Contains("23.10.2026", ru);
    }

    /// <summary>
    /// День 4: отсутствующий ключ с аргументами — тот же читаемый fallback
    /// (имя ключа), без FormatException: в имени ключа нет плейсхолдеров.
    /// </summary>
    [Fact]
    public void GetString_WithFormatArgs_MissingKey_ReturnsKeyName_WithoutFormatting()
    {
        Localizer.Culture = new CultureInfo("ru");

        var result = Localizer.GetString("No_Such_Key_Abc123", 1, 2);

        Assert.Equal("No_Such_Key_Abc123", result);
    }
}
