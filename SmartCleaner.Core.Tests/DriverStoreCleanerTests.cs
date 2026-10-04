using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.WinSxS;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 15 — M8 (ROADMAP 214-219, findings M8): дедуп драйверов DriverStore.
/// Порядок блоков в выводе «pnputil /enum-drivers» не гарантирует хронологию,
/// поэтому жертвы определяются сравнением версии (Version.TryParse) и даты
/// (тай-брейк), а не позицией в списке: новейший в группе защищён,
/// строго старые — предвыбраны. Фикстуры воспроизводят реальный формат
/// Win10/11: метка строки версии «Driver Version»/«Версия драйвера»
/// (формат зафиксирован на реальной машине: ru-локаль, 129 пакетов),
/// строка версии — «MM/DD/YYYY x.y.z.w».
/// День 18 — срез B: контрактные тесты команды pnputil через стаб
/// RecordingCommandExecutor — полное имя утилиты (SystemToolLocator),
/// аргументы, таймаут; деградация при отказе исполнителя.
/// </summary>
public class DriverStoreCleanerTests
{
    // en-US, «перемешанный» порядок: новейший пакет стоит ПЕРВЫМ, самый старый — последним.
    // Позиционная логика «все, кроме последнего» пометила бы новейший как устаревший.
    private const string EnglishShuffledOutput = """
        Microsoft PnP Utility (Microsoft)

        Published Name:     oem5.inf
        Original Name:      nv_dispi.inf
        Provider Name:      NVIDIA
        Class Name:         Display adapters
        Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
        Driver Version:     07/08/2026 31.0.15.52
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        Published Name:     oem2.inf
        Original Name:      nv_dispi.inf
        Provider Name:      NVIDIA
        Class Name:         Display adapters
        Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
        Driver Version:     03/15/2023 31.0.15.17
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher

        Published Name:     oem9.inf
        Original Name:      nv_dispi.inf
        Provider Name:      NVIDIA
        Class Name:         Display adapters
        Class GUID:         {4d36e968-e325-11ce-bfc1-08002be10318}
        Driver Version:     10/30/2021 30.0.14.7
        Signer Name:        Microsoft Windows Hardware Compatibility Publisher
        """;

    // ru-RU: пара amdacpbus2.inf — реальный дубль из вывода pnputil на живой машине,
    // порядок «перемешан»: новейшая версия стоит первой.
    private const string RussianShuffledOutput = """
        Служебная программа PnP (Майкрософт)

        Опубликованное имя:     oem86.inf
        Исходное имя:           amdacpbus2.inf
        Имя поставщика:         AMD
        Имя класса:             System
        GUID класса:            {4d36e97d-e325-11ce-bfc1-08002be10318}
        Версия драйвера:        07/08/2026 7.2610.1.1
        Имя подписавшего:       Microsoft Windows Hardware Compatibility Publisher
        Атрибуты:               Universal
        Версия WHCP:            Нет данных

        Опубликованное имя:     oem94.inf
        Исходное имя:           amdacpbus2.inf
        Имя поставщика:         AMD
        Имя класса:             System
        GUID класса:            {4d36e97d-e325-11ce-bfc1-08002be10318}
        Версия драйвера:        03/08/2026 7.0.3.59
        Имя подписавшего:       Microsoft Windows Hardware Compatibility Publisher
        Атрибуты:               Universal
        Версия WHCP:            Нет данных
        """;

    [Fact]
    public void ParseAndMark_ShuffledEnglishOrder_NewestProtected_OldPreselected()
    {
        var items = DriverStoreCleaner.ParsePnputilOutput(EnglishShuffledOutput);
        DriverStoreCleaner.MarkDuplicateGroups(items);

        var newest = Assert.Single(items, d => d.PublishedName == "oem5.inf");
        Assert.Equal("31.0.15.52", newest.DriverVersion);
        Assert.Equal("07/08/2026", newest.DriverDate);
        Assert.False(newest.IsOldDuplicate);
        Assert.False(newest.IsSelected);
        Assert.True(newest.IsCurrent);

        var older = Assert.Single(items, d => d.PublishedName == "oem2.inf");
        Assert.True(older.IsOldDuplicate);
        Assert.True(older.IsSelected);
        Assert.False(older.IsCurrent);

        var oldest = Assert.Single(items, d => d.PublishedName == "oem9.inf");
        Assert.True(oldest.IsOldDuplicate);
        Assert.True(oldest.IsSelected);
    }

    [Fact]
    public void ParseAndMark_RussianLocale_VersionAndDateParsed_NewestProtected()
    {
        var items = DriverStoreCleaner.ParsePnputilOutput(RussianShuffledOutput);
        DriverStoreCleaner.MarkDuplicateGroups(items);

        var newest = Assert.Single(items, d => d.PublishedName == "oem86.inf");
        Assert.Equal("7.2610.1.1", newest.DriverVersion);
        Assert.Equal("07/08/2026", newest.DriverDate);
        Assert.False(newest.IsOldDuplicate);
        Assert.False(newest.IsSelected);
        Assert.True(newest.IsCurrent);

        var older = Assert.Single(items, d => d.PublishedName == "oem94.inf");
        Assert.True(older.IsOldDuplicate);
        Assert.True(older.IsSelected);
        Assert.False(older.IsCurrent);
    }

    [Fact]
    public void MarkDuplicateGroups_EqualVersions_NewerDateWins()
    {
        // Версии одинаковые: новее тот, у кого дата позже; он стоит первым (перемешанно).
        var items = new List<DriverStoreItem>
        {
            New("oemA.inf", "ext.inf", "Net", "1.0.0.2", "07/08/2026"),
            New("oemB.inf", "ext.inf", "Net", "1.0.0.2", "03/08/2026")
        };

        DriverStoreCleaner.MarkDuplicateGroups(items);

        Assert.False(items[0].IsOldDuplicate);
        Assert.False(items[0].IsSelected);
        Assert.True(items[1].IsOldDuplicate);
        Assert.True(items[1].IsSelected);
    }

    [Fact]
    public void MarkDuplicateGroups_UnparseableVersion_StayProtected()
    {
        // Версию распарсить нельзя — доказать «старее» невозможно, запись не помечаем (fail-safe).
        var items = new List<DriverStoreItem>
        {
            New("oemA.inf", "ext.inf", "Net", "N/A", "07/08/2026"),
            New("oemB.inf", "ext.inf", "Net", "1.0.0.2", "03/08/2026")
        };

        DriverStoreCleaner.MarkDuplicateGroups(items);

        Assert.False(items[0].IsOldDuplicate);
        Assert.False(items[0].IsSelected);
        Assert.False(items[1].IsOldDuplicate);
    }

    [Fact]
    public void MarkDuplicateGroups_SingleEntry_NotPreselected()
    {
        var items = new List<DriverStoreItem>
        {
            New("oem7.inf", "solo.inf", "System", "1.0.0.0", "07/08/2026")
        };

        DriverStoreCleaner.MarkDuplicateGroups(items);

        Assert.False(items[0].IsOldDuplicate);
        Assert.False(items[0].IsSelected);
    }

    [Fact]
    public void MarkDuplicateGroups_SameInfNameDifferentClass_NotDuplicates()
    {
        // Идентичность пакета — имя INF + класс: совпало только имя — это разные пакеты.
        var items = new List<DriverStoreItem>
        {
            New("oemA.inf", "dup.inf", "Display adapters", "1.0.0.1", "07/08/2026"),
            New("oemB.inf", "dup.inf", "Printers", "1.0.0.2", "07/08/2026")
        };

        DriverStoreCleaner.MarkDuplicateGroups(items);

        Assert.False(items[0].IsOldDuplicate);
        Assert.False(items[1].IsOldDuplicate);
    }

    [Fact]
    public void BuildDeleteArguments_ByDefault_NoForce()
    {
        // День 15 — M8: по умолчанию /force нет — используемый системой драйвер
        // pnputil обязан честно отказаться удалять, а не быть вырванным.
        var item = New("oem9.inf", "nv_dispi.inf", "Display adapters", "30.0.14.7", "10/30/2021");

        var args = DriverStoreCleaner.BuildDeleteArguments(item, forceConfirmed: false);

        Assert.Equal("/delete-driver oem9.inf /uninstall", args);
    }

    [Fact]
    public void BuildDeleteArguments_ExplicitConfirmationOnly_AddsForce()
    {
        var item = New("oem9.inf", "nv_dispi.inf", "Display adapters", "30.0.14.7", "10/30/2021");

        var args = DriverStoreCleaner.BuildDeleteArguments(item, forceConfirmed: true);

        Assert.Equal("/delete-driver oem9.inf /uninstall /force", args);
    }

    // День 18 — срез B: контракт pnputil /enum-drivers через стаб исполнителя.
    [Fact]
    public async Task ScanDriversAsync_IssuesPnputilEnumThroughExecutor_WithTimeout()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(EnglishShuffledOutput);
        var cleaner = new DriverStoreCleaner(executor);

        var drivers = await cleaner.ScanDriversAsync();

        // Стаб возвращает фикстуру: три пакета из EnglishShuffledOutput
        Assert.Equal(3, drivers.Count);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetPnputilPath(), request.FileName);
        Assert.Equal(new[] { "/enum-drivers" }, request.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(1), request.Timeout);
    }

    [Fact]
    public async Task ScanDriversAsync_PnputilOutput_DecodedWithAnsiEncoding()
    {
        // День 19: живой замер на русской Win11 — pnputil /enum-drivers пишет
        // CP1251 (сырые байты метки «Опубликованное имя» = CE EF F3 E1 …), а не
        // OEM-866. Дефолтное декодирование исполнителя зависит от консоли хоста:
        // UTF-8-консоль → mojibake → метки в ParsePnputilOutput не матчатся →
        // пустой список драйверов. Запрос обязан нести явную ANSI-кодировку
        // (GetACP), как schtasks несёт OEM (День 8).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        if (System.Text.Encoding.GetEncoding(0).CodePage != 1251)
            return; // ANSI-страница системы не 1251 — проверка неприменима

        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(RussianShuffledOutput);
        var cleaner = new DriverStoreCleaner(executor);

        var drivers = await cleaner.ScanDriversAsync();

        Assert.Equal(2, drivers.Count);
        var request = Assert.Single(executor.Requests);
        Assert.NotNull(request.StandardOutputEncoding);
        Assert.Equal(1251, request.StandardOutputEncoding!.CodePage);
    }

    [Fact]
    public async Task ScanDriversAsync_ExecutorFailure_ReturnsEmptyListWithoutThrow()
    {
        // Отказ запуска утилиты — пустой список, а не исключение наружу:
        // сканирование в UI обязано деградировать честно
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(-1, "Не удалось запустить процесс");
        var cleaner = new DriverStoreCleaner(executor);

        var drivers = await cleaner.ScanDriversAsync();

        Assert.Empty(drivers);
    }

    private static DriverStoreItem New(
        string publishedName, string originalName, string className, string version, string date) => new()
    {
        PublishedName = publishedName,
        OriginalName = originalName,
        ProviderName = "Test Provider",
        ClassName = className,
        DriverVersion = version,
        DriverDate = date
    };
}
