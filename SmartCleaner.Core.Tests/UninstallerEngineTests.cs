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
        var engine = new UninstallerEngine();
        var app = new InstalledAppItem
        {
            DisplayName = "Untrusted App",
            UninstallString = Path.Combine(Path.GetTempPath(), $"untrusted-{Guid.NewGuid():N}", "uninstall.exe")
        };

        var (success, message) = await engine.UninstallAppAsync(app, silent: true);

        Assert.False(success);
        Assert.Contains("Тихая деинсталляция отклонена", message);
        Assert.Contains("доверенной зоне", message);
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
}
