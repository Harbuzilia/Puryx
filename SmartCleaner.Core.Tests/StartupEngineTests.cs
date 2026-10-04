using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Startup;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class StartupEngineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\My App\\app.exe\" --minimized",
                "C:\\Program Files\\My App\\app.exe", "--minimized")]
    [InlineData("\"C:\\App\\app.exe\"",
                "C:\\App\\app.exe", "")]
    [InlineData("C:\\App\\app.exe /background",
                "C:\\App\\app.exe", "/background")]
    [InlineData("app.exe", "app.exe", "")]
    [InlineData("  \"C:\\Tools\\t.exe\"  run --now  ",
                "C:\\Tools\\t.exe", "run --now")]
    public void ParseCommand_SplitsPathAndArguments(
        string command, string expectedPath, string expectedArgs)
    {
        var (filePath, arguments) = StartupEngine.ParseCommand(command);

        Assert.Equal(expectedPath, filePath);
        Assert.Equal(expectedArgs, arguments);
    }

    [Fact]
    public void ParseCommand_QuotedPathWithNestedQuotes_UsesFirstClosingQuote()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\A\\b.exe\" \"arg with spaces\"");

        Assert.Equal("C:\\A\\b.exe", filePath);
        Assert.Equal("\"arg with spaces\"", arguments);
    }

    [Fact]
    public void ParseCommand_UnclosedQuote_FallsBackToWholeString()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\App\\unclosed");

        Assert.Equal("\"C:\\App\\unclosed", filePath);
        Assert.Equal("", arguments);
    }

    [Fact]
    public void ParseCommand_QuotedPathWithArgsAfterSpaces_TrimsArguments()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\A\\b.exe\"   ");

        Assert.Equal("C:\\A\\b.exe", filePath);
        Assert.Equal("", arguments);
    }

    /// <summary>
    /// Реальные байты вывода «schtasks /query /fo CSV /NH» в OEM-кодировке cp866
    /// (первые 5 строк с машины разработчика; имена задач латиницей, статусы — кириллицей).
    /// </summary>
    private static readonly byte[] SchtasksCp866Fixture =
    [
        // "\AMD Install Manager - Check For Updates","04.10.2026 3:00:00","Готово"
        0x22, 0x5C, 0x41, 0x4D, 0x44, 0x20, 0x49, 0x6E, 0x73, 0x74, 0x61, 0x6C, 0x6C, 0x20, 0x4D, 0x61,
        0x6E, 0x61, 0x67, 0x65, 0x72, 0x20, 0x2D, 0x20, 0x43, 0x68, 0x65, 0x63, 0x6B, 0x20, 0x46, 0x6F,
        0x72, 0x20, 0x55, 0x70, 0x64, 0x61, 0x74, 0x65, 0x73, 0x22, 0x2C, 0x22, 0x30, 0x34, 0x2E, 0x31,
        0x30, 0x2E, 0x32, 0x30, 0x32, 0x36, 0x20, 0x33, 0x3A, 0x30, 0x30, 0x3A, 0x30, 0x30, 0x22, 0x2C,
        0x22, 0x83, 0xAE, 0xE2, 0xAE, 0xA2, 0xAE, 0x22, 0x0D, 0x0A,
        // "\AMDInstallLauncher","N/A","Готово"
        0x22, 0x5C, 0x41, 0x4D, 0x44, 0x49, 0x6E, 0x73, 0x74, 0x61, 0x6C, 0x6C, 0x4C, 0x61, 0x75, 0x6E,
        0x63, 0x68, 0x65, 0x72, 0x22, 0x2C, 0x22, 0x4E, 0x2F, 0x41, 0x22, 0x2C, 0x22, 0x83, 0xAE, 0xE2,
        0xAE, 0xA2, 0xAE, 0x22, 0x0D, 0x0A,
        // "\AMDScoSupportTypeUpdate","N/A","Готово"
        0x22, 0x5C, 0x41, 0x4D, 0x44, 0x53, 0x63, 0x6F, 0x53, 0x75, 0x70, 0x70, 0x6F, 0x72, 0x74, 0x54,
        0x79, 0x70, 0x65, 0x55, 0x70, 0x64, 0x61, 0x74, 0x65, 0x22, 0x2C, 0x22, 0x4E, 0x2F, 0x41, 0x22,
        0x2C, 0x22, 0x83, 0xAE, 0xE2, 0xAE, 0xA2, 0xAE, 0x22, 0x0D, 0x0A,
        // "\AMDScoSupportTypeUpdate","N/A","Готово"
        0x22, 0x5C, 0x41, 0x4D, 0x44, 0x53, 0x63, 0x6F, 0x53, 0x75, 0x70, 0x70, 0x6F, 0x72, 0x74, 0x54,
        0x79, 0x70, 0x65, 0x55, 0x70, 0x64, 0x61, 0x74, 0x65, 0x22, 0x2C, 0x22, 0x4E, 0x2F, 0x41, 0x22,
        0x2C, 0x22, 0x83, 0xAE, 0xE2, 0xAE, 0xA2, 0xAE, 0x22, 0x0D, 0x0A,
        // "\ASUS Hotplug Controller","N/A","Выполняется"
        0x22, 0x5C, 0x41, 0x53, 0x55, 0x53, 0x20, 0x48, 0x6F, 0x74, 0x70, 0x6C, 0x75, 0x67, 0x20, 0x43,
        0x6F, 0x6E, 0x74, 0x72, 0x6F, 0x6C, 0x6C, 0x65, 0x72, 0x22, 0x2C, 0x22, 0x4E, 0x2F, 0x41, 0x22,
        0x2C, 0x22, 0x82, 0xEB, 0xAF, 0xAE, 0xAB, 0xAD, 0xEF, 0xA5, 0xE2, 0xE1, 0xEF, 0x22, 0x0D, 0x0A
    ];

    [Fact]
    public void DecodeSchtasksOutput_Cp866Fixture_DecodesReadableCyrillicLines()
    {
        var lines = StartupEngine.DecodeSchtasksOutput(SchtasksCp866Fixture);

        Assert.Equal(5, lines.Count);
        Assert.Equal("\"\\AMD Install Manager - Check For Updates\",\"04.10.2026 3:00:00\",\"Готово\"", lines[0]);
        Assert.Equal("\"\\AMDInstallLauncher\",\"N/A\",\"Готово\"", lines[1]);
        Assert.Equal("\"\\AMDScoSupportTypeUpdate\",\"N/A\",\"Готово\"", lines[2]);
        Assert.Equal("\"\\AMDScoSupportTypeUpdate\",\"N/A\",\"Готово\"", lines[3]);
        Assert.Equal("\"\\ASUS Hotplug Controller\",\"N/A\",\"Выполняется\"", lines[4]);
    }

    // ─── Парсер «schtasks /query /fo CSV /NH /V» (подробный формат, 28 колонок) ──
    //
    // Реальные строки живого вывода «schtasks /query /fo CSV /NH /V» с машины
    // разработчика (OEM cp866 → UTF-8, 04.10.2026, узел WIN-VINSKQP4LQQ).
    // Формат /V (порядок сверен по строке заголовка живого вывода):
    // [0]=«Имя узла», [1]=«Имя задачи», [3]=«Состояние», [8]=«Задача для выполнения».
    // Контент колонок сверен с XML задач (schtasks /query /tn … /xml):
    // schtasks оборачивает КАЖДОЕ поле в кавычки, НЕ экранируя внутренние кавычки
    // (это не RFC 4180): «""C:\…exe" -arg» — обрамляющая кавычка поля + кавычка
    // контента; задача Realtek зарегистрирована с задвоенными кавычками
    // («""C:\…exe""» в XML задачи) — поле начинается с трёх кавычек.

    /// <summary>Живая строка /V: задача AMD — команда в кавычках, статус «Готово».</summary>
    private const string AmdVerboseLine =
        @"""WIN-VINSKQP4LQQ"",""\AMD Install Manager - Check For Updates"",""04.10.2026 3:00:00"",""Готово"",""Интерактивный/фоновый"",""02.10.2026 3:02:01"",""0"",""Advanced Micro Devices"",""""C:\Program Files\AMD\AMDInstallManager\AMDInstallManager.exe"" -CheckForUpdates"",""C:\Program Files\AMD\AMDInstallManager\"",""Installs updates for the AMD Graphics Driver"",""Включено"",""Отключено"",""Не запускать при питании от батареи"",""СИСТЕМА"",""Отключено"",""Отключено"",""Планирование данных в этом формате недоступно."",""Ежедневно "",""3:00:00"",""04.10.2026"",""N/A"",""Каждые 1 дн."",""N/A"",""Отключено"",""Отключено"",""Отключено"",""Отключено""";

    /// <summary>Живая строка /V: задача Realtek — команда зарегистрирована с задвоенными кавычками, статус «Выполняется».</summary>
    private const string RealtekDoubledQuotesLine =
        @"""WIN-VINSKQP4LQQ"",""\RtkAudUService64_BG"",""N/A"",""Выполняется"",""Интерактивный/фоновый"",""03.10.2026 13:33:43"",""267009"",""Realtek"",""""""C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_24b55e23c63ee280\RtkAudUService64.exe"""" -background"",""Н/Д"",""Н/Д"",""Включено"",""Отключено"","""",""Пользователи"",""Отключено"",""Отключено"",""Планирование данных в этом формате недоступно."",""При входе в систему"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A""";

    /// <summary>Живая строка /V: задача ASUS — команда без кавычек, статус «Выполняется».</summary>
    private const string AsusHotplugLine =
        @"""WIN-VINSKQP4LQQ"",""\ASUS Hotplug Controller"",""N/A"",""Выполняется"",""Интерактивный/фоновый"",""03.10.2026 13:33:12"",""267009"",""ASUSTek Computer Inc"",""C:\Program Files\ASUS\ASUS Hotplug Controller\AsHotplugCtrl.exe "",""Н/Д"",""Н/Д"",""Включено"",""Отключено"","""",""Администраторы"",""Отключено"",""Отключено"",""Планирование данных в этом формате недоступно."",""При входе в систему"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A""";

    /// <summary>Живая строка /V: системная задача \Microsoft\ — должна отфильтровываться по колонке 1 («Имя задачи»), а не по колонке 0 («Имя узла»).</summary>
    private const string MicrosoftMediaSharingLine =
        @"""WIN-VINSKQP4LQQ"",""\Microsoft\Windows\Windows Media Sharing\UpdateLibrary"",""N/A"",""Готово"",""Интерактивный/фоновый"",""30.11.1999 0:00:00"",""267011"",""Microsoft Corporation"",""""%ProgramFiles%\Windows Media Player\wmpnscfg.exe"" "",""Н/Д"",""Эта задача обновляет кэшированный список папок и разрешения безопасности для всех новых файлов в общей библиотеке мультимедиа пользователя."",""Включено"",""Отключено"","""",""Прошедшие проверку"",""Отключено"",""72:00:00"",""Планирование данных в этом формате недоступно."",""При начале события"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A"",""N/A""";

    [Fact]
    public void ParseCsvLine_QuotedFields_UnwrapsOuterQuotes()
    {
        var fields = StartupEngine.ParseCsvLine(@"""a"",""b"",""c""");

        Assert.Equal(3, fields.Length);
        Assert.Equal("a", fields[0]);
        Assert.Equal("b", fields[1]);
        Assert.Equal("c", fields[2]);
    }

    [Fact]
    public void ParseCsvLine_CommaInsideQuotedField_StaysInField()
    {
        var fields = StartupEngine.ParseCsvLine(@"""a"",""x,y"",""c""");

        Assert.Equal(3, fields.Length);
        Assert.Equal("x,y", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_EmptyQuotedField_IsEmptyString()
    {
        var fields = StartupEngine.ParseCsvLine(@"""a"","""",""c""");

        Assert.Equal(3, fields.Length);
        Assert.Equal("", fields[1]);
    }

    [Fact]
    public void ParseCsvLine_VerboseAmdLine_ColumnsAndCommandField()
    {
        var fields = StartupEngine.ParseCsvLine(AmdVerboseLine);

        Assert.Equal("WIN-VINSKQP4LQQ", fields[0]);                        // «Имя узла» — НЕ имя задачи
        Assert.Equal(@"\AMD Install Manager - Check For Updates", fields[1]); // «Имя задачи»
        Assert.Equal("Готово", fields[3]);                                 // «Состояние»
        Assert.Equal(28, fields.Length);                                   // 28 колонок формата /V
        // «Задача для выполнения»: обрамление поля снято, кавычки контента сохранены
        Assert.Equal(@"""C:\Program Files\AMD\AMDInstallManager\AMDInstallManager.exe"" -CheckForUpdates", fields[8]);
    }

    [Fact]
    public void ParseCsvLine_DoubledQuotesInCommand_PreservedAsContent()
    {
        var fields = StartupEngine.ParseCsvLine(RealtekDoubledQuotesLine);

        // Контент поля соответствует XML задачи: команда как зарегистрирована — с задвоенными кавычками
        Assert.Equal(@"""""C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_24b55e23c63ee280\RtkAudUService64.exe"""" -background", fields[8]);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_VerboseAmdTask_MapsColumnsAndSplitsCommand()
    {
        var item = StartupEngine.TryParseTaskSchedulerCsvLine(AmdVerboseLine);

        Assert.NotNull(item);
        Assert.Equal("AMD Install Manager - Check For Updates", item!.Name); // имя задачи, а не «Имя узла»
        Assert.Equal(@"C:\Program Files\AMD\AMDInstallManager\AMDInstallManager.exe", item.FilePath);
        Assert.Equal("-CheckForUpdates", item.Arguments);
        Assert.Equal(@"""C:\Program Files\AMD\AMDInstallManager\AMDInstallManager.exe"" -CheckForUpdates", item.Command);
        Assert.Equal(StartupSource.TaskScheduler, item.Source);
        Assert.True(item.IsEnabled);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_MicrosoftTask_ReturnsNull()
    {
        // Баг: фильтр \Microsoft\ проверял fields[0] (HostName) и никогда не срабатывал
        Assert.Null(StartupEngine.TryParseTaskSchedulerCsvLine(MicrosoftMediaSharingLine));
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_RealtekDoubledQuotedCommand_SplitsFilePathAndArguments()
    {
        var item = StartupEngine.TryParseTaskSchedulerCsvLine(RealtekDoubledQuotesLine);

        Assert.NotNull(item);
        Assert.Equal("RtkAudUService64_BG", item!.Name);
        Assert.Equal(@"C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_24b55e23c63ee280\RtkAudUService64.exe", item.FilePath);
        Assert.Equal("-background", item.Arguments);
        Assert.Equal(@"""""C:\Windows\System32\DriverStore\FileRepository\realtekservice.inf_amd64_24b55e23c63ee280\RtkAudUService64.exe"""" -background", item.Command);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_RunningTaskWithUnquotedCommand_ReturnsItemWithFullCommand()
    {
        var item = StartupEngine.TryParseTaskSchedulerCsvLine(AsusHotplugLine);

        Assert.NotNull(item);
        Assert.Equal("ASUS Hotplug Controller", item!.Name);
        Assert.Equal(StartupSource.TaskScheduler, item.Source);
        // CSV-слой обязан сохранить команду целиком (хвостовой пробел — как в живом выводе);
        // корректный разбор FilePath/Args для путей с пробелами без кавычек — отдельная задача ParseCommand
        Assert.Equal(@"C:\Program Files\ASUS\ASUS Hotplug Controller\AsHotplugCtrl.exe ", item.Command);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"short\",\"line\"")]
    public void TryParseTaskSchedulerCsvLine_EmptyOrTooFewColumns_ReturnsNull(string line)
    {
        Assert.Null(StartupEngine.TryParseTaskSchedulerCsvLine(line));
    }

    [Fact]
    public void ParseCommand_DoubledQuotedPath_SplitsPathAndArguments()
    {
        // Реальный кейс: задача Realtek зарегистрирована с «""C:\…exe""» вместо «"C:\…exe"»
        var (filePath, arguments) = StartupEngine.ParseCommand(@"""""C:\Tools\app.exe"""" -run --now");

        Assert.Equal(@"C:\Tools\app.exe", filePath);
        Assert.Equal("-run --now", arguments);
    }

    // ─── ScanAllAsync: контракт ICommandExecutor (День 19, срез C) ───────────

    [Fact]
    public async Task ScanAllAsync_SchtasksQuery_GoesThroughCommandExecutor()
    {
        // День 19: опрос планировщика через контракт исполнителя. Запрос обязан
        // нести абсолютный путь schtasks (SystemToolLocator), argv-аргументы,
        // явную OEM-кодировку декодирования (День 8: дефолт .NET зависит от
        // консоли хоста и в UTF-8-консоли/WPF-контексте ломает русский вывод) и
        // ограниченный таймаут. Реестр/папки сканируются как прежде.
        var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "\"H\",\"\\TaskFromStub\",\"xx\",\"Готово\",\"a\",\"b\",\"c\",\"d\",\"C:\\Tool\\tool.exe /run\"\r\n"
        });
        var engine = new StartupEngine(executor);

        var items = await engine.ScanAllAsync();

        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetSchtasksPath(), request.FileName);
        Assert.Equal(new[] { "/query", "/fo", "CSV", "/NH", "/V" }, request.Arguments);
        Assert.Equal(StartupEngine.GetSchtasksOutputEncoding(), request.StandardOutputEncoding);
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);

        // Строка из стаба распарсена в элемент планировщика (реестровые элементы
        // машины не мешают: проверяем конкретный элемент по источнику и имени)
        var stubItem = Assert.Single(items, i => i.Source == StartupSource.TaskScheduler);
        Assert.Equal("TaskFromStub", stubItem.Name);
        Assert.Equal(@"C:\Tool\tool.exe", stubItem.FilePath);
        Assert.Equal("/run", stubItem.Arguments);
    }

    [Fact]
    public async Task ScanAllAsync_SchtasksFailure_RegistryItemsStillReturned()
    {
        // schtasks недоступен (ненулевой exit) — источник планировщика пуст,
        // остальное сканирование не ломается (прежняя семантика catch-all).
        var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult { ExitCode = 1 });
        var engine = new StartupEngine(executor);

        var items = await engine.ScanAllAsync();

        Assert.Single(executor.Requests);
        Assert.DoesNotContain(items, i => i.Source == StartupSource.TaskScheduler);
        Assert.Contains(items, i => i.Source != StartupSource.TaskScheduler);
    }

    // ─── День 21 — L2 (ROADMAP): Disable/Enable/Delete для TaskScheduler ──
    //
    // Прежде источник «Планировщик заданий» в Disable/Enable/Delete попадал в
    // default: return false — молча не поддержан. Теперь операции идут через
    // schtasks: /Change /TN <путь> /DISABLE|/ENABLE, /Delete /TN <путь> /F.

    /// <summary>Элемент планировщика как после сканирования: Name — последний сегмент, TaskName — полный путь.</summary>
    private static StartupItem TaskSchedulerItem(string taskName) => new()
    {
        Name = Path.GetFileName(taskName),
        TaskName = taskName,
        Source = StartupSource.TaskScheduler,
        IsEnabled = true,
        FilePath = @"C:\Stub\tool.exe"
    };

    [Fact]
    public async Task DisableItemAsync_TaskScheduler_IssuesSchtasksChangeDisable()
    {
        var executor = new RecordingCommandExecutor();
        var engine = new StartupEngine(executor);
        var item = TaskSchedulerItem(@"\Vendor\UpdateTask");

        Assert.True(await engine.DisableItemAsync(item));
        Assert.False(item.IsEnabled);

        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetSchtasksPath(), request.FileName);
        Assert.Equal(new[] { "/Change", "/TN", @"\Vendor\UpdateTask", "/DISABLE" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(15), request.Timeout);
    }

    [Fact]
    public async Task EnableItemAsync_TaskScheduler_IssuesSchtasksChangeEnable()
    {
        var executor = new RecordingCommandExecutor();
        var engine = new StartupEngine(executor);
        var item = TaskSchedulerItem(@"\Vendor\UpdateTask");
        item.IsEnabled = false;

        Assert.True(await engine.EnableItemAsync(item));
        Assert.True(item.IsEnabled);

        var request = Assert.Single(executor.Requests);
        Assert.Equal(new[] { "/Change", "/TN", @"\Vendor\UpdateTask", "/ENABLE" }, request.Arguments);
    }

    [Fact]
    public async Task DeleteItemAsync_TaskScheduler_IssuesSchtasksDeleteForce()
    {
        // /F обязателен: без него schtasks в неинтерактивном режиме спрашивает
        // подтверждение на консоли и отказывает
        var executor = new RecordingCommandExecutor();
        var engine = new StartupEngine(executor);
        var item = TaskSchedulerItem(@"\Vendor\OneTimeTask");

        Assert.True(await engine.DeleteItemAsync(item));

        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetSchtasksPath(), request.FileName);
        Assert.Equal(new[] { "/Delete", "/TN", @"\Vendor\OneTimeTask", "/F" }, request.Arguments);
    }

    [Fact]
    public async Task DisableItemAsync_TaskSchedulerSchtasksFails_ReturnsFalseAndKeepsState()
    {
        // Отказ schtasks (права/нет задачи) — false, состояние элемента не врёт
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "ERROR: Access is denied.");
        var engine = new StartupEngine(executor);
        var item = TaskSchedulerItem(@"\Vendor\UpdateTask");

        Assert.False(await engine.DisableItemAsync(item));
        Assert.True(item.IsEnabled);
    }

    [Fact]
    public async Task DisableItemAsync_TaskSchedulerWithoutTaskPath_ReturnsFalse()
    {
        // Элемент планировщика без полного пути задачи (/TN по нему невозможен) —
        // честный false, а не попытка schtasks с пустым именем
        var executor = new RecordingCommandExecutor();
        var engine = new StartupEngine(executor);
        var item = new StartupItem { Name = "NoPath", Source = StartupSource.TaskScheduler, IsEnabled = true };

        Assert.False(await engine.DisableItemAsync(item));
        Assert.Empty(executor.Requests);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_PreservesFullTaskPathForSchtasksOperations()
    {
        // Полный путь задачи (колонка 1 «Имя задачи») обязан сохраняться в TaskName:
        // schtasks /Change /TN адресует задачу путём \Папка\Задача, а Name — лишь
        // последний сегмент (для отображения)
        var item = StartupEngine.TryParseTaskSchedulerCsvLine(AmdVerboseLine);

        Assert.NotNull(item);
        Assert.Equal(@"\AMD Install Manager - Check For Updates", item!.TaskName);
    }

    // ==== День 22 — финализация: мутации вывода schtasks (0277b68, M2-регрессии) ====

    [Fact]
    public void ParseCsvLine_EmptyUnquotedFields_PreservedAsEmptyStrings()
    {
        // Пустые поля без кавычек: «a,,c» — три поля, среднее пустое
        // (несуществующее поле = сдвиг колонок /v-формата)
        var fields = StartupEngine.ParseCsvLine("a,,c");

        Assert.Equal(new[] { "a", "", "c" }, fields);
    }

    [Fact]
    public void ParseCsvLine_QuoteInsideFieldWithoutSeparator_StaysInContent()
    {
        // Задвоенная кавычка в середине поля (не перед разделителем и не в
        // конце строки): по правилу wrap-aware парсера это контент — обе
        // кавычки сохраняются как есть (не RFC-4180-свёртка «"" в «"»)
        var fields = StartupEngine.ParseCsvLine(@"""a""""b""");

        Assert.Single(fields);
        Assert.Equal("a\"\"b", fields[0]);
    }

    [Fact]
    public void DecodeSchtasksOutput_LfOnlyLineEndings_SplitsLines()
    {
        // schtasks при перенаправлении/pipe может отдавать LF-only строки:
        // декодер обязан резать и по «\n», не только по «\r\n»
        var text = "\"\\TaskA\",\"N/A\",\"Готово\"\n\"\\TaskB\",\"N/A\",\"Готово\"";
        var bytes = StartupEngine.GetSchtasksOutputEncoding().GetBytes(text);

        var lines = StartupEngine.DecodeSchtasksOutput(bytes);

        Assert.Equal(2, lines.Count);
        Assert.Equal("\"\\TaskA\",\"N/A\",\"Готово\"", lines[0]);
        Assert.Equal("\"\\TaskB\",\"N/A\",\"Готово\"", lines[1]);
    }

    [Fact]
    public void DecodeSchtasksOutput_StrayHighBytes_DoNotBreakLineSplitting()
    {
        // Битые/нестандартные байты OEM-вывода: декодер не падает (замена
        // символов вместо исключения), разбиение строк сохраняется,
        // ASCII-префикс не искажается
        var bytes = new byte[]
        {
            0x22, 0x5C, 0x41, 0x0D, 0x0A,                          // «"\A» + CRLF
            0x22, 0x5C, 0x42, 0x98, 0x99, 0x9B, 0xFF, 0x0D, 0x0A   // «"\B» + мусор + CRLF
        };

        var lines = StartupEngine.DecodeSchtasksOutput(bytes);

        Assert.Equal(2, lines.Count);
        Assert.Equal("\"\\A", lines[0]);
        Assert.StartsWith("\"\\B", lines[1]);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_MultilineNoteSplit_HeadParsesTailFiltered()
    {
        // Примечание задачи с переводом строки: ReadLine режет запись пополам
        // (известное ограничение построчного CSV-разбора schtasks, 0277b68).
        // Голова с целыми колонками 0-8 разбирается в элемент; хвост-продолжение
        // обязан отфильтроваться (короткая строка), а не падать и не порождать
        // фантомный элемент
        const string head =
            "\"HOST\",\"\\MyApp\",\"N/A\",\"Готово\",\"Интерактивный\"," +
            "\"N/A\",\"0\",\"Author\",\"C:\\Tools\\app.exe -arg\",\"C:\\wd\",\"Начало примечания";
        const string tail = "окончание примечания\",\"Включено\",\"Отключено\"";

        var item = StartupEngine.TryParseTaskSchedulerCsvLine(head);
        var tailItem = StartupEngine.TryParseTaskSchedulerCsvLine(tail);

        Assert.NotNull(item);
        Assert.Equal("MyApp", item!.Name);
        Assert.Equal(@"C:\Tools\app.exe", item.FilePath);
        Assert.Equal("-arg", item.Arguments);
        Assert.Null(tailItem);
    }

    [Fact]
    public void TryParseTaskSchedulerCsvLine_EmptyTaskToRun_IsFiltered()
    {
        // Пустая колонка «Задача для выполнения»: элемент не создаётся
        // (фильтр пустой команды), а не элемент с FilePath из пустой строки
        const string line =
            "\"HOST\",\"\\MyApp\",\"N/A\",\"Готово\",\"Интерактивный\"," +
            "\"N/A\",\"0\",\"Author\",\"\",\"C:\\wd\",\"Примечание\"";

        var item = StartupEngine.TryParseTaskSchedulerCsvLine(line);

        Assert.Null(item);
    }
}
