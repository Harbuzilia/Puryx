using System.Text.RegularExpressions;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 16б — M7 (binary planting): инвариант исходников Core.
/// Системные утилиты Windows (sc/net/netsh/ipconfig/schtasks/dism/pnputil/compact/
/// powercfg/powershell) обязаны запускаться только по абсолютному пути из
/// SystemToolLocator: запуск по неквалифицированному имени ищет exe сначала
/// в каталоге приложения, а в portable-распространении он доступен пользователю
/// на запись — подмена утилиты с правами elevated.
///
/// Инвариант сканирует исходники SmartCleaner.Core и запрещает вне
/// Helpers/SystemToolLocator.cs — единственного санкционированного места, где
/// имена комбинируются с системным каталогом в абсолютный путь, — строковые
/// литералы, равные имени утилиты: с .exe всегда, без .exe — только в
/// launch-контексте строки (FileName/RunCommand/ProcessStartInfo/...).
/// Так ловится и «FileName = "sc.exe",», и передача имени параметром
/// (RunCommand("net.exe", ...)), и «FileName = "powercfg"», — но не ложные
/// совпадения вроде Contains("compact") в NL-парсере или "powershell" как
/// сегмента пути в CliInspector.
///
/// Внешние dev-инструменты (docker, npm) под инвариант НЕ попадают: их нет в
/// системном каталоге, PATH-поиск — осознанная семантика для них.
/// </summary>
public class SystemToolLocatorInvariantTests
{
    // Детерминированный слой: литерал, равный имени утилиты С .exe, в коде
    // недопустим всегда — это гарантированно форма запуска/передачи имени.
    private static readonly Regex UnqualifiedToolNameWithExeRegex = new(
        "\"(?<tool>sc|net|netsh|ipconfig|schtasks|dism|pnputil|compact|powercfg|powershell)\\.exe\"",
        RegexOptions.Compiled);

    // Эвристический слой: литерал БЕЗ .exe — только в launch-контексте (строка
    // упоминает запуск процесса). Отделяет RunCommand("powercfg", ...) от
    // Contains("compact") в NL-парсере или "powershell" как сегмента пути.
    private static readonly Regex LaunchContextRegex = new(
        "RunCommand|RunCommandCapture|FileName|Process\\.Start|ProcessStartInfo",
        RegexOptions.Compiled);

    private static readonly Regex UnqualifiedToolBareNameRegex = new(
        "\"(?<tool>sc|net|netsh|ipconfig|schtasks|dism|pnputil|compact|powercfg|powershell)\"",
        RegexOptions.Compiled);

    [Fact]
    public void CoreSources_ContainNoUnqualifiedWindowsUtilityNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreRoot = Path.Combine(repositoryRoot, "SmartCleaner.Core");
        Assert.True(Directory.Exists(coreRoot), $"каталог исходников Core не найден: {coreRoot}");

        var violations = ScanProjectForUnqualifiedToolNames(Path.Combine(repositoryRoot, "SmartCleaner.Core"));
        Assert.True(violations.Count == 0,
            "Неквалифицированные имена системных утилит в Core (binary planting, M7). " +
            "Запуск возможен только через SmartCleaner.Core.Helpers.SystemToolLocator:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// День 23 — перенос находки Дня 19: инвариант M7 распространён на исходники
    /// App (срез C консолидации покрывал Core, App- ViewModel ускользал):
    /// powershell.exe в CliInspectorViewModel.OpenTerminal запускался по
    /// неквалифицированному имени — binary planting в portable-сценарии.
    /// </summary>
    [Fact]
    public void AppSources_ContainNoUnqualifiedWindowsUtilityNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "SmartCleaner.App");
        Assert.True(Directory.Exists(appRoot), $"каталог исходников App не найден: {appRoot}");

        var violations = ScanProjectForUnqualifiedToolNames(appRoot);
        Assert.True(violations.Count == 0,
            "Неквалифицированные имена системных утилит в App (binary planting, M7 — перенос Дня 19). " +
            "Запуск возможен только через SmartCleaner.Core.Helpers.SystemToolLocator:\n" +
            string.Join("\n", violations));
    }

    /// <summary>
    /// Сканирует исходники проекта (*.cs, кроме obj/bin) на строковые литералы,
    /// равные имени системной утилиты (см. доктайп класса).
    /// </summary>
    private static List<string> ScanProjectForUnqualifiedToolNames(string projectRoot)
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repositoryRoot, file);

            // Единственный санкционированный владелец имён утилит: там имена
            // комбинируются с системным каталогом в абсолютный путь.
            if (relative.EndsWith(@"Helpers\SystemToolLocator.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            // Сгенерированные артефакты сборки не являются исходниками
            var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Contains("obj") || segments.Contains("bin"))
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                // Строчные, XML-doc и внутриблочные комментарии не являются кодом:
                // упоминание утилиты в комментарии допустимо.
                if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("/*"))
                    continue;

                var violation =
                    UnqualifiedToolNameWithExeRegex.IsMatch(lines[i])
                    || (LaunchContextRegex.IsMatch(lines[i]) && UnqualifiedToolBareNameRegex.IsMatch(lines[i]));
                if (violation)
                {
                    violations.Add($"{relative}:{i + 1}: {trimmed}");
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Поднимается от каталога тестовой сборки до корня репозитория (SmartCleaner.sln).
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SmartCleaner.sln")))
        {
            dir = dir.Parent!;
        }

        Assert.True(dir is not null,
            $"корень репозитория (SmartCleaner.sln) не найден над {AppContext.BaseDirectory}");
        return dir!.FullName;
    }
}
