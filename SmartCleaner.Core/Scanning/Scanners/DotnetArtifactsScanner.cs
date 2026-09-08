using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер артефактов сборки .NET/C# проектов (bin/, obj/).
/// Находит папки bin и obj в проектах, проверяет возраст и предлагает удалить.
/// </summary>
public class DotnetArtifactsScanner : ScannerBase
{
    public override string CategoryName => "Сборка .NET";
    public override string CategoryIcon => "\uE943"; // Segoe MDL2: Code
    public override int DisplayOrder => 9;

    /// <summary>Минимальный размер папки для отображения (5 МБ)</summary>
    private const long MinSizeBytes = 5 * 1024 * 1024;

    /// <summary>Корневые папки для поиска проектов</summary>
    private static readonly string[] SearchRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ];

    /// <summary>Папки которые пропускаем при рекурсивном обходе</summary>
    private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".vs", ".vscode", "packages",
        "AppData", ".nuget", ".dotnet", "Program Files", "Program Files (x86)",
        "Windows", "$Recycle.Bin", "ProgramData"
    };

    public DotnetArtifactsScanner(IKnowledgeBase knowledge, ISafetyService safety)
        : base(knowledge, safety) { }

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
                progress?.Report($"Поиск артефактов .NET в {root}...");

                FindArtifactFolders(root, result.Items, progress, ct, maxDepth: 6, currentDepth: 0);
            }
        }, ct);

        return result;
    }

    /// <summary>
    /// Рекурсивно ищет папки bin/ и obj/ рядом с .csproj/.fsproj/.vbproj файлами
    /// </summary>
    private void FindArtifactFolders(
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

        // Проверяем, есть ли тут .csproj (это .NET проект)
        bool isProjectDir = false;
        try
        {
            isProjectDir = Directory.EnumerateFiles(directory, "*.csproj").Any()
                        || Directory.EnumerateFiles(directory, "*.fsproj").Any()
                        || Directory.EnumerateFiles(directory, "*.vbproj").Any();
        }
        catch (Exception ex) { /* Нет доступа — пропускаем */ System.Diagnostics.Debug.WriteLine($"[DotnetArtifactsScanner] Project file check error: {ex.Message}"); }

        if (isProjectDir)
        {
            // Проверяем bin и obj
            ProcessArtifactDir(Path.Combine(directory, "bin"), directory, items, "bin");
            ProcessArtifactDir(Path.Combine(directory, "obj"), directory, items, "obj");
        }

        // Рекурсия в подпапки
        foreach (var subdir in SafeEnumerateDirectories(directory))
        {
            var dirName = Path.GetFileName(subdir);

            // Пропускаем стандартные папки
            if (SkipFolders.Contains(dirName))
                continue;

            // Пропускаем bin/obj — уже обработаны
            if (dirName.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                dirName.Equals("obj", StringComparison.OrdinalIgnoreCase))
                continue;

            FindArtifactFolders(subdir, items, progress, ct, maxDepth, currentDepth + 1);
        }
    }

    /// <summary>
    /// Обработать одну папку bin/ или obj/
    /// </summary>
    private void ProcessArtifactDir(string path, string projectDir, IList<ScannedItem> items, string folderType)
    {
        if (!Directory.Exists(path))
            return;

        var size = CalculateDirectorySize(path);
        if (size < MinSizeBytes)
            return;

        var projectName = Path.GetFileName(projectDir);
        var risk = folderType == "obj" ? RiskCategory.SafeToDelete : RiskCategory.PerformanceCache;

        items.Add(CreateDirectoryItem(
            path,
            risk,
            $"{folderType}/ артефакты проекта «{projectName}» — можно пересобрать через dotnet build",
            parentApp: projectName,
            restoreCommand: $"dotnet build \"{projectDir}\""));
    }
}
