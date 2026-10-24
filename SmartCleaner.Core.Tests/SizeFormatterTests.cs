using System.Globalization;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class SizeFormatterTests : IDisposable
{
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public SizeFormatterTests()
    {
        // День 6б (фаза L): юниты размера идут через Localizer — существующие
        // ассерты пиннят RU-юниты; Culture фиксируется для детерминизма,
        // Dispose восстанавливает (паттерн LocalizationTests)
        Localizer.Culture = new CultureInfo("ru");
    }

    public void Dispose()
    {
        Localizer.Culture = _originalLocalizerCulture;
    }

    private static string BuildExpected(string fmt)
    {
        // SizeFormatter uses current culture for decimal separator
        var sep = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        return fmt.Replace(".", sep);
    }

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(1, "1 Б")]
    [InlineData(512, "512 Б")]
    [InlineData(1023, "1023 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(1536, "1.5 КБ")]
    [InlineData(2048, "2 КБ")]
    [InlineData(1024 * 1024, "1 МБ")]
    [InlineData(1024 * 1024 + 512 * 1024, "1.5 МБ")]
    [InlineData(1024L * 1024 * 1024, "1 ГБ")]
    [InlineData(1024L * 1024 * 1024 * 2, "2 ГБ")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1 ТБ")]
    [InlineData(1024L * 1024 * 1024 * 1024 * 3, "3 ТБ")]
    public void Format_VariousSizes_ReturnsExpectedString(long bytes, string expected)
    {
        var result = SizeFormatter.Format(bytes);
        Assert.Equal(BuildExpected(expected), result);
    }

    [Fact]
    public void Format_NegativeBytes_ReturnsNegativeFormatted()
    {
        var result = SizeFormatter.Format(-1);
        Assert.Contains("-", result);
    }

    [Fact]
    public void Format_MaxLongValue_DoesNotThrow()
    {
        var result = SizeFormatter.Format(long.MaxValue);
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Theory]
    [InlineData(1024L * 1024 * 1024 * 5 + 512 * 1024 * 1024, "5.5 ГБ")]
    [InlineData(1024L * 1024 * 100, "100 МБ")]
    [InlineData(1024L * 1024 * 1024 * 10, "10 ГБ")]
    public void Format_PreciseBoundaries_MatchesExpectation(long bytes, string expected)
    {
        var result = SizeFormatter.Format(bytes);
        Assert.Equal(BuildExpected(expected), result);
    }

    /// <summary>
    /// День 6б (фаза L): юниты размера локализуются через Localizer.
    /// Найдено смоуком: «0 Б» в статс-пилюлях EN-главной. EN-культура
    /// обязана давать B/KB/MB/GB/TB, RU — Б/КБ/МБ/ГБ/ТБ.
    /// </summary>
    [Fact]
    public void Format_EnglishNeutralCulture_UsesEnglishUnits()
    {
        Localizer.Culture = CultureInfo.InvariantCulture;

        Assert.Equal(BuildExpected("1.5 KB"), SizeFormatter.Format(1536));
        Assert.Equal(BuildExpected("2 GB"), SizeFormatter.Format(1024L * 1024 * 1024 * 2));
    }

    [Fact]
    public void Format_RussianCulture_UsesRussianUnits()
    {
        Localizer.Culture = new CultureInfo("ru");

        Assert.Equal(BuildExpected("1.5 КБ"), SizeFormatter.Format(1536));
        Assert.Equal(BuildExpected("2 ГБ"), SizeFormatter.Format(1024L * 1024 * 1024 * 2));
    }
}