using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.WinSxS;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 7 — H4 (ROADMAP): честные метрики.
/// Парсер вывода «dism /Online /Cleanup-Image /AnalyzeComponentStore» обязан извлекать
/// фактические значения (en/ru локали; ru использует запятую как десятичный разделитель
/// и кириллические единицы) и никогда не подставлять выдуманные числа:
/// битый/пустой вывод — «н/д», а не «~7.5 ГБ».
/// День 18 — срез B: контрактные тесты команды DISM через стаб
/// RecordingCommandExecutor — полное имя утилиты (SystemToolLocator),
/// аргументы, таймаут; честные причины недоступности (740/таймаут).
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

    // День 18 — срез B: вывод DISM для контракта через стаб — минимальный
    // parseable-набор (en-локаль, без прогресс-строк).
    private const string AnalyzeOutput = """
        Actual Size of Component Store : 8.12 GB
        Shared with Windows : 6.01 GB
        Backups and Disabled Features : 1.28 GB
        Component Store Cleanup Recommended : Yes
        The operation completed successfully.
        """;

    [Fact]
    public async Task AnalyzeComponentStoreAsync_IssuesDismThroughExecutor_WithArgumentsAndTimeout()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(AnalyzeOutput);
        var engine = new WinSxSEngine(executor);

        var result = await engine.AnalyzeComponentStoreAsync();

        Assert.True(result.AnalysisAvailable);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetDismPath(), request.FileName);
        Assert.Equal(new[] { "/Online", "/Cleanup-Image", "/AnalyzeComponentStore" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(10), request.Timeout);
    }

    [Fact]
    public async Task AnalyzeComponentStoreAsync_ElevationFailure_HonestReasonWithExitCode()
    {
        // Без прав администратора DISM завершается ошибкой 740 — пользователь
        // обязан видеть «нужны права», а не безликое «нет данных»
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(740, "Error: 740\r\nThe requested operation requires elevation.");
        var engine = new WinSxSEngine(executor);

        var result = await engine.AnalyzeComponentStoreAsync();

        Assert.False(result.AnalysisAvailable);
        Assert.Contains("права администратора", result.AnalysisUnavailableReason);
        Assert.Contains("740", result.AnalysisUnavailableReason);
    }

    [Fact]
    public async Task AnalyzeComponentStoreAsync_Timeout_HonestTimeoutReason()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueResult(new CommandExecutionResult { ExitCode = -1, TimedOut = true });
        var engine = new WinSxSEngine(executor);

        var result = await engine.AnalyzeComponentStoreAsync();

        Assert.False(result.AnalysisAvailable);
        Assert.Contains("таймаут", result.AnalysisUnavailableReason);
    }

    // ==== День 21 — L5 (ROADMAP): /ResetBase — только явный opt-in ====

    [Fact]
    public void RunComponentCleanupAsync_DefaultParameter_IsFalse()
    {
        // Контракт «необратимая операция выключена по умолчанию»: дефолт
        // параметра resetBase обязан быть false — включение только явным
        // выбором пользователя с подтверждением
        var parameter = typeof(WinSxSEngine)
            .GetMethod(nameof(WinSxSEngine.RunComponentCleanupAsync))!
            .GetParameters()
            .Single(p => p.Name == "resetBase");

        Assert.NotNull(parameter.DefaultValue);
        Assert.Equal(false, parameter.DefaultValue);
    }

    [Fact]
    public void BuildComponentCleanupArguments_WithoutResetBase_OmitsResetBase()
    {
        // Без /ResetBase: консолидация компонентов — обратимая очистка
        var args = WinSxSEngine.BuildComponentCleanupArguments(resetBase: false);

        Assert.Equal("/Online /Cleanup-Image /StartComponentCleanup", args);
        Assert.DoesNotContain("ResetBase", args, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildComponentCleanupArguments_WithResetBase_AppendsResetBase()
    {
        var args = WinSxSEngine.BuildComponentCleanupArguments(resetBase: true);

        Assert.Equal("/Online /Cleanup-Image /StartComponentCleanup /ResetBase", args);
    }

    // Ожидание «8.12 ГБ» без дублирования логики форматирования в тесте
    private static string SizeFormatterExpectation(double gb) =>
        SmartCleaner.Core.Helpers.SizeFormatter.Format((long)(gb * Gb));
}
