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
/// Инвариант сканирует исходники SmartCleaner.Core и запрещает строковые литералы,
/// равные имени утилиты (с .exe или без), вне Helpers/SystemToolLocator.cs —
/// единственного санкционированного места, где имя комбинируется с системным
/// каталогом в абсолютный путь. Подсветка совпадает не только с «FileName = "...",»,
/// но и с передачей имени параметром (RunCommand("net.exe", ...)) — так регрессии
/// не вернутся ни через одну форму запуска.
///
/// Внешние dev-инструменты (docker, npm) под инвариант НЕ попадают: их нет в
/// системном каталоге, PATH-поиск — осознанная семантика для них.
/// </summary>
public class SystemToolLocatorInvariantTests
{
    private static readonly Regex UnqualifiedToolNameRegex = new(
        "\"(?<tool>sc|net|netsh|ipconfig|schtasks|dism|pnputil|compact|powercfg|powershell)(\\.exe)?\"",
        RegexOptions.Compiled);

    [Fact]
    public void CoreSources_ContainNoUnqualifiedWindowsUtilityNames()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreRoot = Path.Combine(repositoryRoot, "SmartCleaner.Core");
        Assert.True(Directory.Exists(coreRoot), $"каталог исходников Core не найден: {coreRoot}");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories))
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

                if (UnqualifiedToolNameRegex.IsMatch(lines[i]))
                {
                    violations.Add($"{relative}:{i + 1}: {trimmed}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Неквалифицированные имена системных утилит в Core (binary planting, M7). " +
            "Запуск возможен только через SmartCleaner.Core.Helpers.SystemToolLocator:\n" +
            string.Join("\n", violations));
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
