using SmartCleaner.Core.Helpers;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 5 — M7 (ROADMAP), День 16б (расширение на все Core-утилиты): binary planting.
/// Системные утилиты должны резолвиться только по абсолютному пути из настоящего
/// системного каталога: запуск по неквалифицированному имени ищет exe сначала
/// в каталоге приложения, а в portable-распространении он доступен пользователю
/// на запись — подмена утилиты с правами elevated.
/// </summary>
public class WinSxSSystemToolLocatorTests
{
    [Theory]
    [InlineData("dism")]
    [InlineData("pnputil")]
    [InlineData("compact")]
    [InlineData("sc")]
    [InlineData("net")]
    [InlineData("netsh")]
    [InlineData("ipconfig")]
    [InlineData("schtasks")]
    [InlineData("powercfg")]
    public void SystemTool_ResolvedByAbsolutePathFromNativeSystemDirectory(string tool)
    {
        var path = Resolve(tool);

        // Абсолютный путь: запуск не зависит от PATH и каталога приложения
        Assert.True(Path.IsPathRooted(path), $"путь должен быть абсолютным: {path}");
        Assert.Equal(tool + ".exe", Path.GetFileName(path), ignoreCase: true);

        // Путь лежит в настоящем системном каталоге: System32
        // (или SysNative, если 32-битный процесс на 64-битной ОС)
        var directory = Path.GetDirectoryName(path);
        Assert.NotNull(directory);
        var nativeDirs = new[]
        {
            Environment.SystemDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysNative")
        };
        Assert.Contains(nativeDirs, d => string.Equals(d, directory, StringComparison.OrdinalIgnoreCase));

        // Утилита реально существует по этому пути (комплект Windows)
        Assert.True(File.Exists(path), $"утилита должна существовать: {path}");
    }

    private static string Resolve(string tool) => tool switch
    {
        "dism" => SystemToolLocator.GetDismPath(),
        "pnputil" => SystemToolLocator.GetPnputilPath(),
        "compact" => SystemToolLocator.GetCompactPath(),
        "sc" => SystemToolLocator.GetScPath(),
        "net" => SystemToolLocator.GetNetPath(),
        "netsh" => SystemToolLocator.GetNetshPath(),
        "ipconfig" => SystemToolLocator.GetIpconfigPath(),
        "schtasks" => SystemToolLocator.GetSchtasksPath(),
        "powercfg" => SystemToolLocator.GetPowercfgPath(),
        _ => throw new ArgumentException($"неизвестная утилита: {tool}", nameof(tool))
    };

    [Fact]
    public void WindowsPowerShell_ResolvedByAbsolutePathFromSystemPowerShellDirectory()
    {
        var path = SystemToolLocator.GetWindowsPowerShellPath();

        // Абсолютный путь: запуск не зависит от PATH и каталога приложения
        Assert.True(Path.IsPathRooted(path), $"путь должен быть абсолютным: {path}");
        Assert.Equal("powershell.exe", Path.GetFileName(path), ignoreCase: true);

        // Системный Windows PowerShell живёт в WindowsPowerShell\v1.0 настоящего
        // системного каталога (System32 либо SysNative для 32-битного процесса).
        var directory = Path.GetDirectoryName(path);
        Assert.NotNull(directory);
        Assert.EndsWith(
            Path.Combine("WindowsPowerShell", "v1.0"),
            directory,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"утилита должна существовать: {path}");
    }
}
