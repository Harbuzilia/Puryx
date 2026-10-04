using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Uninstaller;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 3 — H1 (ROADMAP): элевация деинсталлятора.
/// Команда из реестра недоверенна по умолчанию (HKCU-ветку Uninstall может записать
/// любой процесс без прав): авто-элевация и тихий запуск разрешены только доверенным exe
/// (доверенная директория Program Files/Windows или наличие Authenticode-подписи).
/// </summary>
public class UninstallerEngineTests
{
    // ─── SplitArguments: argv-разбор аргументной части команды из реестра ────

    [Theory]
    [InlineData("/S /D=C:\\App", new[] { "/S", "/D=C:\\App" })]
    [InlineData("/X{B5E6A1C1-9F3E-4A2D-8C1B-0E5D7F9A2B4C} /qb", new[] { "/X{B5E6A1C1-9F3E-4A2D-8C1B-0E5D7F9A2B4C}", "/qb" })]
    [InlineData("run --now", new[] { "run", "--now" })]
    [InlineData("", new string[] { })]
    [InlineData("   ", new string[] { })]
    [InlineData("/S", new[] { "/S" })]
    public void SplitArguments_PlainTokens_SplitByWhitespace(string arguments, string[] expected)
    {
        Assert.Equal(expected, UninstallerEngine.SplitArguments(arguments));
    }

    [Fact]
    public void SplitArguments_QuotedArgumentWithSpaces_SingleToken()
    {
        // Аргументная часть вида «"C:\Log Dir\uninstall.log"» после ParseCommand:
        // кавычки снимаются, путь с пробелом — один токен.
        Assert.Equal(
            new[] { @"C:\Log Dir\uninstall.log" },
            UninstallerEngine.SplitArguments("\"C:\\Log Dir\\uninstall.log\""));
    }

    [Fact]
    public void SplitArguments_MixedQuotedAndPlainTokens()
    {
        Assert.Equal(
            new[] { "--log", @"C:\Log Dir\out.log", "-v" },
            UninstallerEngine.SplitArguments("--log \"C:\\Log Dir\\out.log\" -v"));
    }

    [Fact]
    public void SplitArguments_EscapedQuoteInsideQuotedToken_IsLiteralQuote()
    {
        // «a "say \"hi\"" b» → три токена, кавычка внутри второго — литерал
        Assert.Equal(
            new[] { "a", "say \"hi\"", "b" },
            UninstallerEngine.SplitArguments("a \"say \\\"hi\\\"\" b"));
    }

    [Fact]
    public void SplitArguments_BackslashesNotBeforeQuote_AreLiteral()
    {
        // Обратные слэши путей Windows, за которыми НЕ кавычка, — литералы:
        // «C:\path\ /S» — два токена, первый сохраняет хвостовой бэкслэш.
        Assert.Equal(
            new[] { @"C:\path\", "/S" },
            UninstallerEngine.SplitArguments("C:\\path\\ /S"));
    }

    [Fact]
    public void SplitArguments_UnclosedQuote_ConsumesRestAsSingleToken()
    {
        // Мусор реестра: незакрытая кавычка — остаток строки один токен
        // (поведение CommandLineToArgvW для незакрытой кавычки).
        Assert.Equal(
            new[] { @"C:\Bad path\un.exe" },
            UninstallerEngine.SplitArguments("\"C:\\Bad path\\un.exe"));
    }

    [Fact]
    public void SplitArguments_BackslashQuoteOutsideQuotes_IsEscapedLiteralQuote()
    {
        // «\"» вне кавычек — литеральная кавычка (одиночный токен из одного символа)
        Assert.Equal(
            new[] { "\"" },
            UninstallerEngine.SplitArguments("\\\""));
    }

    // ─── ParseCommand: разбор команды с кавычками/аргументами ────────────────

    [Theory]
    [InlineData(@"""C:\Program Files\App\Uninstall.exe"" /S /D=C:\App", @"C:\Program Files\App\Uninstall.exe", @"/S /D=C:\App")]
    [InlineData(@"""C:\Program Files\App\Uninstall.exe""", @"C:\Program Files\App\Uninstall.exe", "")]
    [InlineData(@"C:\App\Uninstall.exe /S", @"C:\App\Uninstall.exe", "/S")]
    [InlineData(@"C:\App\Uninstall.exe", @"C:\App\Uninstall.exe", "")]
    [InlineData(@"MsiExec.exe /X{B5E6A1C1-9F3E-4A2D-8C1B-0E5D7F9A2B4C}", @"MsiExec.exe", @"/X{B5E6A1C1-9F3E-4A2D-8C1B-0E5D7F9A2B4C}")]
    public void ParseCommand_SplitsFileNameAndArguments(string commandLine, string expectedFile, string expectedArgs)
    {
        var (file, args) = UninstallerEngine.ParseCommand(commandLine);

        Assert.Equal(expectedFile, file);
        Assert.Equal(expectedArgs, args);
    }

    [Fact]
    public void ParseCommand_UnterminatedQuote_StripsQuoteAndSplitsBySpace()
    {
        // Реестр содержит мусор вида «"C:\App\Uninstall.exe /S» — незакрытая кавычка.
        // Ведущая кавычка должна убираться, остаток парсится как команда без кавычек
        // (раньше FileName возвращался вместе с кавычкой — запуск ломался).
        var (file, args) = UninstallerEngine.ParseCommand(@"""C:\App\Uninstall.exe /S");

        Assert.Equal(@"C:\App\Uninstall.exe", file);
        Assert.Equal("/S", args);
    }

    // ─── ResolveExecutable: резолв exe в доверенную зону ─────────────────────

    [Fact]
    public void ResolveExecutable_AbsolutePath_PassesThrough()
    {
        var path = @"C:\Some\App\Uninstall.exe";

        Assert.Equal(path, UninstallTrustPolicy.ResolveExecutable(path));
    }

    [Fact]
    public void ResolveExecutable_MsiExecWithoutPath_ResolvesFromSystemDirectory()
    {
        // UninstallString MSI-приложений не содержит пути: «MsiExec.exe /X{...}».
        // Резолв из системного каталога, а не из PATH/каталога приложения (binary planting).
        var resolved = UninstallTrustPolicy.ResolveExecutable("MsiExec.exe");

        Assert.Equal(Path.Combine(Environment.SystemDirectory, "MsiExec.exe"), resolved);
    }

    [Fact]
    public void ResolveExecutable_UnknownName_PassesThroughUnchanged()
    {
        var name = "DefinitelyMissingTool12345.exe";

        Assert.Equal(name, UninstallTrustPolicy.ResolveExecutable(name));
    }

    // ─── IsTrustedLocation: лексическая проверка директории ──────────────────

    [Fact]
    public void IsTrustedLocation_ProgramFilesSubtree_IsTrusted()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        Assert.True(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(pf, "Vendor", "App", "Uninstall.exe")));
        Assert.True(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(pf, "Uninstall.exe")));
    }

    [Fact]
    public void IsTrustedLocation_WindowsSubtree_IsTrusted()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(win, "System32", "tool.exe")));
        Assert.True(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(win, "tool.exe")));
    }

    [Fact]
    public void IsTrustedLocation_ProgramFilesX86_IsTrusted()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        Assert.True(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(pf86, "App", "Uninstall.exe")));
    }

    [Fact]
    public void IsTrustedLocation_UserTemp_NotTrusted()
    {
        Assert.False(UninstallTrustPolicy.IsTrustedLocation(Path.Combine(Path.GetTempPath(), "App", "Uninstall.exe")));
    }

    [Fact]
    public void IsTrustedLocation_SiblingSpoofDirectory_NotTrusted()
    {
        // «C:\Program FilesEvil\...» не должен проходить проверку по префиксу «C:\Program Files»
        var spoof = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"Evil\App\Uninstall.exe";

        Assert.False(UninstallTrustPolicy.IsTrustedLocation(spoof));
    }

    [Fact]
    public void IsTrustedLocation_TraversalOutOfTrustRoot_NotTrusted()
    {
        var escape = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\..\..\Temp\x.exe";

        Assert.False(UninstallTrustPolicy.IsTrustedLocation(escape));
    }

    [Theory]
    [InlineData("Uninstall.exe")]
    [InlineData("")]
    public void IsTrustedLocation_RelativeOrEmpty_NotTrusted(string candidate)
    {
        Assert.False(UninstallTrustPolicy.IsTrustedLocation(candidate));
    }

    [Fact]
    public void IsTrustedLocation_Null_NotTrusted()
    {
        Assert.False(UninstallTrustPolicy.IsTrustedLocation(null!));
    }

    // ─── IsTrustedExecutable: доверенная директория ИЛИ подпись ──────────────

    [Fact]
    public void IsTrustedExecutable_TrustedLocation_DoesNotProbeSignature()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var exe = Path.Combine(pf, "Vendor", "Uninstall.exe");
        var probed = false;

        Assert.True(UninstallTrustPolicy.IsTrustedExecutable(exe, _ => { probed = true; return true; }));
        Assert.False(probed); // подпись не проверяется — достаточно доверенной директории
    }

    [Fact]
    public void IsTrustedExecutable_UntrustedLocationSigned_Trusted()
    {
        var exe = Path.Combine(Path.GetTempPath(), "App", "Uninstall.exe");

        Assert.True(UninstallTrustPolicy.IsTrustedExecutable(exe, _ => true));
    }

    [Fact]
    public void IsTrustedExecutable_UntrustedLocationUnsigned_NotTrusted()
    {
        var exe = Path.Combine(Path.GetTempPath(), "App", "Uninstall.exe");

        Assert.False(UninstallTrustPolicy.IsTrustedExecutable(exe, _ => false));
    }

    [Fact]
    public void IsTrustedExecutable_UnqualifiedSystemTool_MsiExec_Trusted()
    {
        // «MsiExec.exe» без пути резолвится в System32 → доверенная зона
        Assert.True(UninstallTrustPolicy.IsTrustedExecutable("MsiExec.exe"));
    }

    [Fact]
    public void IsTrustedExecutable_Empty_NotTrusted()
    {
        Assert.False(UninstallTrustPolicy.IsTrustedExecutable(""));
    }

    // ─── HasAuthenticodeSignature: факт наличия подписи ──────────────────────

    [Fact]
    public void HasAuthenticodeSignature_UnsignedFile_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"unsigned-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            Assert.False(UninstallTrustPolicy.HasAuthenticodeSignature(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HasAuthenticodeSignature_SignedBinary_ReturnsTrue()
    {
        // Системные файлы Windows подписаны каталогом (catalog signing), а не встроенной
        // подписью — CreateFromSignedFile их не видит. Встроенно-подписанный бинарник,
        // который есть на любой машине, собирающей это решение — dotnet.exe.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe")
        };
        var signed = candidates.FirstOrDefault(File.Exists);
        if (signed is null)
        {
            return; // окружение без встроенно-подписанных бинарников — проверка неприменима
        }

        Assert.True(UninstallTrustPolicy.HasAuthenticodeSignature(signed));
    }

    [Fact]
    public void HasAuthenticodeSignature_MissingFile_ReturnsFalse()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        Assert.False(UninstallTrustPolicy.HasAuthenticodeSignature(missing));
    }

    // ─── UninstallAppAsync: честный контракт запуска ──────────────────────────

    [Fact]
    public async Task UninstallAppAsync_SilentUntrustedExe_IsRefusedWithClearMessage()
    {
        // H1: тихий запуск команды из реестра для exe вне доверенной зоны — явный отказ.
        // Путь не существует: реального запуска процесса в тесте нет.
        var executor = new RecordingCommandExecutor();
        var engine = new UninstallerEngine(executor);
        var app = new InstalledAppItem
        {
            DisplayName = "Untrusted App",
            UninstallString = Path.Combine(Path.GetTempPath(), $"untrusted-{Guid.NewGuid():N}", "uninstall.exe")
        };

        var (success, message) = await engine.UninstallAppAsync(app, silent: true);

        Assert.False(success);
        Assert.Contains("Тихая деинсталляция отклонена", message);
        Assert.Contains("доверенной зоне", message);
        // Отказ происходит ДО любого запуска — исполнителю команд ничего не ушло
        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task UninstallAppAsync_MissingUninstallString_ReturnsHonestFailure()
    {
        var engine = new UninstallerEngine();

        var (success, message) = await engine.UninstallAppAsync(new InstalledAppItem { DisplayName = "X" });

        Assert.False(success);
        Assert.Contains("Команда деинсталляции отсутствует", message);
    }

    [Fact]
    public async Task UninstallAppAsync_NullApp_ReturnsHonestFailure()
    {
        var engine = new UninstallerEngine();

        var (success, message) = await engine.UninstallAppAsync(null!);

        Assert.False(success);
        Assert.Contains("Команда деинсталляции отсутствует", message);
    }

    // ─── UninstallAppAsync: контракт ICommandExecutor (День 19, срез C) ──────

    [Fact]
    public async Task UninstallAppAsync_InteractiveLaunch_RoutesThroughCommandExecutor()
    {
        // День 19: основной (безrunas) запуск деинсталлятора идёт через контракт
        // исполнителя — AbsolutePath, ArgumentList (argv-разбор строки реестра),
        // таймаут Infinite (интерактивный деинсталлятор не убивается по таймеру).
        var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult { ExitCode = 0 });
        var engine = new UninstallerEngine(executor);
        var app = new InstalledAppItem
        {
            DisplayName = "Plain App",
            UninstallString = "\"C:\\Temp Plain\\uninstall.exe\" /S /D=C:\\Data"
        };

        var (success, message) = await engine.UninstallAppAsync(app);

        Assert.True(success);
        Assert.Contains("кодом 0", message);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(@"C:\Temp Plain\uninstall.exe", request.FileName);
        Assert.Equal(new[] { "/S", "/D=C:\\Data" }, request.Arguments);
        Assert.Equal(Timeout.InfiniteTimeSpan, request.Timeout);
    }

    [Fact]
    public async Task UninstallAppAsync_ExecutorNonZeroExit_ReturnsFailureWithExitCode()
    {
        var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult { ExitCode = 5 });
        var engine = new UninstallerEngine(executor);
        var app = new InstalledAppItem
        {
            DisplayName = "Failing App",
            UninstallString = @"C:\Temp\uninstall.exe /S"
        };

        var (success, message) = await engine.UninstallAppAsync(app);

        Assert.False(success);
        Assert.Contains("кодом 5", message);
    }

    [Fact]
    public async Task UninstallAppAsync_SilentTrustedApp_UsesQuietUninstallString()
    {
        // Тихий запуск доверенного (лексически — Program Files) exe: QuietUninstallString
        // приоритетнее, путь и аргументы едут исполнителю одним запросом.
        var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult { ExitCode = 0 });
        var engine = new UninstallerEngine(executor);
        var app = new InstalledAppItem
        {
            DisplayName = "Trusted App",
            UninstallString = "\"C:\\Program Files\\App\\uninstall.exe\" /S",
            QuietUninstallString = "\"C:\\Program Files\\App\\uninstall.exe\" /quiet"
        };

        var (success, message) = await engine.UninstallAppAsync(app, silent: true);

        Assert.True(success);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(@"C:\Program Files\App\uninstall.exe", request.FileName);
        Assert.Equal(new[] { "/quiet" }, request.Arguments);
    }
}
