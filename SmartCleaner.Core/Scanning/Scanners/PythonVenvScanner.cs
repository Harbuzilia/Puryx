using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер Python virtual environments (.venv, venv, env, .env).
/// Находит виртуальные среды Python, оценивает их размер и возраст.
/// </summary>
public class PythonVenvScanner : ScannerBase
{
    public override string CategoryName => "Python Venv";
    public override string CategoryIcon => "\uE943"; // Segoe MDL2: Code
    public override int DisplayOrder => 10;

    /// <summary>Минимальный размер venv для отображения (10 МБ)</summary>
    private const long MinSizeBytes = 10 * 1024 * 1024;

    /// <summary>Минимальный возраст последнего доступа (дни)</summary>
    private const int MinAgeDays = 14;

    /// <summary>Имена папок виртуальных сред</summary>
    private static readonly HashSet<string> VenvFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".venv", "venv", "env", ".env"
    };

    /// <summary>Корневые папки для поиска</summary>
    private static readonly string[] SearchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ];

    /// <summary>Папки которые пропускаем при обходе</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", ".vscode", "AppData",
        ".nuget", ".dotnet", "Program Files", "Program Files (x86)",
        "Windows", "$Recycle.Bin", "ProgramData", "bin", "obj"
    };

    public PythonVenvScanner(ISafetyService safety)
        : base(safety) { }

    public override async Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon
        };

        await Task.Run(() =>
        {
            foreach (var root in SearchRoots)
            {
                if (!Directory.Exists(root))
                    continue;

                ct.ThrowIfCancellationRequested();
                progress?.Report("Поиск Python виртуальных сред...");

                FindVenvFolders(root, result.Items, progress, ct, maxDepth: 6, currentDepth: 0);
            }
        }, ct);

        return result;
    }

    /// <summary>
    /// Рекурсивно ищет папки виртуальных сред Python
    /// </summary>
    private void FindVenvFolders(
        string directory,
        IList<ScannedItem> items,
        IProgress<string>? progress,
        CancellationToken ct,
        int maxDepth,
        int currentDepth)
    {
        if (currentDepth >= maxDepth)
            return;

        ct.ThrowIfCancellationRequested();

        foreach (var subdir in SafeEnumerateDirectories(directory))
        {
            var dirName = Path.GetFileName(subdir);

            // Пропускаем стандартные папки
            if (SkipFolders.Contains(dirName))
                continue;

            // Это venv?
            if (VenvFolderNames.Contains(dirName) && IsVirtualEnv(subdir))
            {
                ProcessVenv(subdir, directory, items);
                continue; // Не рекурсим вглубь venv
            }

            // Рекурсия
            FindVenvFolders(subdir, items, progress, ct, maxDepth, currentDepth + 1);
        }
    }

    /// <summary>
    /// Проверяет является ли папка Python virtual environment (ищет pyvenv.cfg)
    /// </summary>
    private static bool IsVirtualEnv(string path)
    {
        return File.Exists(Path.Combine(path, "pyvenv.cfg"));
    }

    /// <summary>
    /// Обработать найденную виртуальную среду
    /// </summary>
    private void ProcessVenv(string venvPath, string parentDir, IList<ScannedItem> items)
    {
        var size = CalculateDirectorySize(venvPath);
        if (size < MinSizeBytes)
            return;

        var lastAccess = GetLastAccess(venvPath);
        var daysSinceAccess = (DateTime.Now - lastAccess).TotalDays;
        var projectName = Path.GetFileName(parentDir);

        // Определяем риск: старые venv — безопасно, свежие — кэш
        RiskCategory risk;
        string description;

        if (daysSinceAccess > MinAgeDays)
        {
            risk = RiskCategory.SafeToDelete;
            description = $"Виртуальная среда проекта «{projectName}» — не использовалась {(int)daysSinceAccess} дней, можно пересоздать";
        }
        else
        {
            risk = RiskCategory.PerformanceCache;
            description = $"Виртуальная среда проекта «{projectName}» — активная (использовалась {(int)daysSinceAccess} дней назад)";
        }

        // Проверяем requirements.txt для restore command
        string? restoreCommand = null;
        var requirementsPath = Path.Combine(parentDir, "requirements.txt");
        if (File.Exists(requirementsPath))
        {
            restoreCommand = $"python -m venv .venv && .venv\\Scripts\\activate && pip install -r requirements.txt";
        }
        else
        {
            restoreCommand = "python -m venv .venv";
        }

        items.Add(CreateDirectoryItem(
            venvPath,
            risk,
            description,
            parentApp: projectName,
            restoreCommand: restoreCommand));
    }
}
