using SmartCleaner.Core.WinSxS;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 7 — H4 (ROADMAP): честные метрики.
/// Парсер вывода «dism /Online /Cleanup-Image /AnalyzeComponentStore» обязан извлекать
/// фактические значения (en/ru локали; ru использует запятую как десятичный разделитель
/// и кириллические единицы) и никогда не подставлять выдуманные числа:
/// битый/пустой вывод — «н/д», а не «~7.5 ГБ».
/// </summary>
public class WinSxSEngineTests
{
    // Реальный формат вывода DISM en-US (поля задокументированы Microsoft)
    private const string EnglishOutput = """
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.2454

        Image Version: 10.0.26100.2454

        [=====                            100.0%                           ]

        Actual Size of Component Store : 8.12 GB

        Shared with Windows : 6.01 GB
        Backups and Disabled Features : 1.28 GB
        Cache and Temporary Data : 0.38 GB

        Date of Last Cleanup : 2025-09-14 11:23:45

        Component Store Cleanup Recommended : Yes

        The operation completed successfully.
        """;

    // Вывод DISM ru-RU: десятичный разделитель — запятая, единицы — кириллица.
    // Формулировки соответствуют ru-локализации DISM; при отличии на конкретной сборке
    // парсер честно деградирует до «н/д», а не подставляет числа.
    private const string RussianOutput = """
        Инструмент обслуживания образов развертывания и управления ими
        Версия: 10.0.26100.2454

        Версия образа: 10.0.26100.2454

        [=====                            100.0%                           ]

        Фактический размер хранилища компонентов : 8,12 ГБ

        Общий с Windows : 6,01 ГБ
        Резервные копии и отключенные компоненты : 1,28 ГБ
        Кэш и временные данные : 0,38 ГБ

        Дата последней очистки : 2025-09-14 11:23:45

        Рекомендуется очистка хранилища компонентов : Да

        Операция успешно завершена.
        """;

    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void Parse_EnglishOutput_ExtractsActualSizesAndRecommendation()
    {
        var result = WinSxSEngine.ParseAnalyzeComponentStoreOutput(EnglishOutput);

        Assert.True(result.AnalysisAvailable);
        Assert.Equal((long)(8.12 * Gb), result.ActualSizeBytes);
        Assert.Equal((long)(6.01 * Gb), result.SharedWithWindowsBytes);
        Assert.Equal((long)(1.28 * Gb), result.BackupsAndDisabledFeaturesBytes);
        Assert.Equal((long)(1.28 * Gb), result.ReclaimablePackagesBytes);
        Assert.Equal(SizeFormatterExpectation(8.12), result.ActualSizeFormatted);
        Assert.True(result.CleanupRecommended);
    }

    [Fact]
    public void Parse_RussianOutput_CommaDecimalAndCyrillicUnits()
    {
        var result = WinSxSEngine.ParseAnalyzeComponentStoreOutput(RussianOutput);

        Assert.True(result.AnalysisAvailable);
        Assert.Equal((long)(8.12 * Gb), result.ActualSizeBytes);
        Assert.Equal((long)(1.28 * Gb), result.ReclaimablePackagesBytes);
        Assert.True(result.CleanupRecommended);
    }

    [Fact]
    public void Parse_RecommendationNo_CleanupNotRecommended()
    {
        var output = EnglishOutput.Replace("Recommended : Yes", "Recommended : No");

        var result = WinSxSEngine.ParseAnalyzeComponentStoreOutput(output);

        Assert.True(result.AnalysisAvailable);
        Assert.False(result.CleanupRecommended);
    }

    [Fact]
    public void Parse_MBUnits_UsesMegabyteMultiplier()
    {
        var output = EnglishOutput.Replace(
            "Actual Size of Component Store : 8.12 GB",
            "Actual Size of Component Store : 512 MB");

        var result = WinSxSEngine.ParseAnalyzeComponentStoreOutput(output);

        Assert.True(result.AnalysisAvailable);
        Assert.Equal(512L * 1024 * 1024, result.ActualSizeBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("The operation completed successfully.")]
    [InlineData("Error 740\r\nThe requested operation requires elevation.")]
    [InlineData("Deployment Image Servicing and Management tool\r\nVersion: 10.0.26100.2454\r\nImage Version: 10.0.26100.2454")]
    public void Parse_BrokenOrEmptyOutput_NoDataInsteadOfFabricatedNumbers(string output)
    {
        var result = WinSxSEngine.ParseAnalyzeComponentStoreOutput(output);

        Assert.False(result.AnalysisAvailable);
        Assert.Equal(0, result.ActualSizeBytes);
        Assert.Equal(0, result.ReclaimablePackagesBytes);
        Assert.False(result.CleanupRecommended);
        Assert.Equal("н/д", result.ActualSizeFormatted);
        Assert.Equal("н/д", result.ReclaimablePackagesFormatted);
        Assert.NotEmpty(result.AnalysisUnavailableReason);
    }

    // Ожидание «8.12 ГБ» без дублирования логики форматирования в тесте
    private static string SizeFormatterExpectation(double gb) =>
        SmartCleaner.Core.Helpers.SizeFormatter.Format((long)(gb * Gb));
}
