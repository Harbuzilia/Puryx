using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер пользовательской папки — ищет типичный мусор (node_modules, __pycache__,
/// bin/obj, .cache, temp файлы, dist/build) в папке, выбранной пользователем.
/// </summary>
public class CustomFolderScanner : ScannerBase
{
    private readonly ScanConfiguration _config;

    public override string CategoryName => "Выбранная папка";
    public override string CategoryIcon => "\uED25"; // Segoe MDL2: FolderOpen
    public override int DisplayOrder => 99; // Последний
    public override bool IsEnabledByDefault => false; // Включается только когда путь задан

    /// <summary>
    /// Конструктор с инъекцией зависимостей.
    /// </summary>
    /// <param name="config">Общая конфигурация сканирования с CustomScanPath</param>
    /// <param name="safety">Сервис безопасности</param>
    public CustomFolderScanner(ScanConfiguration config, ISafetyService safety) 
        : base(safety)
    {
        _config = config;
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();
        var rootPath = _config.CustomScanPath;

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return new ScanResult
            {
                CategoryName = CategoryName,
                CategoryIcon = CategoryIcon,
                Items = items
            };
        }

        await Task.Run(() =>
        {
            progress?.Report(Localizer.GetString("Scanner_Custom_Progress", rootPath));

            // 1. node_modules
            ScanPattern(rootPath, "node_modules", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_NodeModules",
                "Scanner_Custom_Expl_NodeModules",
                "Scanner_Custom_HowTo_NodeModules",
                "npm install");

            // 2. __pycache__
            ScanPattern(rootPath, "__pycache__", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_PyCache",
                "Scanner_Custom_Expl_PyCache",
                "Scanner_Custom_HowTo_PyCache");

            // 3. .pytest_cache
            ScanPattern(rootPath, ".pytest_cache", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_PytestCache",
                "Scanner_Custom_Expl_PytestCache",
                "Scanner_Custom_HowTo_PytestCache");

            // 4. bin/obj (.NET artifacts)
            ScanPattern(rootPath, "bin", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Bin",
                "Scanner_Custom_Expl_Bin",
                "Scanner_Custom_HowTo_Bin",
                "dotnet build",
                requireSibling: "*.csproj");

            ScanPattern(rootPath, "obj", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Obj",
                "Scanner_Custom_Expl_Obj",
                "Scanner_Custom_HowTo_Obj",
                "dotnet build",
                requireSibling: "*.csproj");

            // 5. dist / build output
            ScanPattern(rootPath, "dist", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Dist",
                "Scanner_Custom_Expl_Dist",
                "Scanner_Custom_HowTo_Dist",
                requireSibling: "package.json");

            ScanPattern(rootPath, "build", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Build",
                "Scanner_Custom_Expl_Build",
                "Scanner_Custom_HowTo_Build",
                requireSibling: "package.json");

            // 6. .cache
            ScanPattern(rootPath, ".cache", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Cache",
                "Scanner_Custom_Expl_Cache",
                "Scanner_Custom_HowTo_Cache");

            // 7. .venv / venv / env
            ScanPattern(rootPath, ".venv", items, ct,
                RiskCategory.PerformanceCache,
                "Scanner_Custom_Desc_DotVenv",
                "Scanner_Custom_Expl_VenvFull",
                "Scanner_Custom_HowTo_VenvFull",
                "python -m venv .venv && pip install -r requirements.txt");

            ScanPattern(rootPath, "venv", items, ct,
                RiskCategory.PerformanceCache,
                "Scanner_Custom_Desc_Venv",
                "Scanner_Custom_Expl_Venv",
                "Scanner_Custom_HowTo_Venv",
                "python -m venv venv");

            // 8. Temp/tmp файлы в корне
            ScanTempFiles(rootPath, items, ct);

            // 9. .next (Next.js cache)
            ScanPattern(rootPath, ".next", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Next",
                "Scanner_Custom_Expl_Next",
                "Scanner_Custom_HowTo_Next",
                "npm run build",
                requireSibling: "next.config.*");

            // 10. .nuxt (Nuxt.js cache)
            ScanPattern(rootPath, ".nuxt", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Nuxt",
                "Scanner_Custom_Expl_Nuxt",
                "Scanner_Custom_HowTo_Nuxt",
                "npm run dev",
                requireSibling: "nuxt.config.*");

            // 11. coverage
            ScanPattern(rootPath, "coverage", items, ct,
                RiskCategory.SafeToDelete,
                "Scanner_Custom_Desc_Coverage",
                "Scanner_Custom_Expl_Coverage",
                "Scanner_Custom_HowTo_Coverage");

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    /// <summary>
    /// Рекурсивно ищет папки с заданным именем и добавляет их в результат.
    /// Если requireSibling не null, папка добавляется только если рядом есть файл с таким паттерном.
    /// </summary>
    /// <param name="rootPath">Корневой путь поиска</param>
    /// <param name="folderName">Имя целевой папки</param>
    /// <param name="items">Список результатов</param>
    /// <param name="ct">Токен отмены</param>
    /// <param name="risk">Категория риска</param>
    /// <param name="descKey">Ключ описания</param>
    /// <param name="explKey">Ключ пояснения (что это и зачем)</param>
    /// <param name="howToKey">Ключ восстановления</param>
    /// <param name="restoreCommand">Команда восстановления</param>
    /// <param name="requireSibling">Паттерн файла-соседа (*.csproj, package.json)</param>
    private void ScanPattern(
        string rootPath, string folderName, List<ScannedItem> items,
        CancellationToken ct, RiskCategory risk, string descKey,
        string? explKey = null, string? howToKey = null,
        string? restoreCommand = null, string? requireSibling = null)
    {
        try
        {
            // Рекурсивный поиск папок с нужным именем через стек
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var currentDir = stack.Pop();

                foreach (var subDir in SafeEnumerateDirectories(currentDir))
                {
                    var dirName = Path.GetFileName(subDir);

                    if (dirName.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Нашли целевую папку — проверяем sibling
                        if (requireSibling != null)
                        {
                            var parentDir = Path.GetDirectoryName(subDir);
                            if (parentDir == null) continue;
                            try
                            {
                                if (!Directory.EnumerateFiles(parentDir, requireSibling).Any())
                                    continue;
                            }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CustomFolderScanner] Sibling dir check error: {ex.Message}"); continue; }
                        }

                        var size = CalculateDirectorySize(subDir);
                        if (size < 1024 * 1024) continue; // < 1 MB

                        items.Add(new ScannedItem
                        {
                            Path = subDir,
                            Size = size,
                            LastAccess = GetLastAccess(subDir),
                            Risk = risk,
                            Description = Localizer.GetString(descKey),
                            Explanation = explKey is null ? null : Localizer.GetString(explKey),
                            HowToRestore = howToKey is null ? null : Localizer.GetString(howToKey),
                            RestoreCommand = restoreCommand,
                            IsDirectory = true,
                            IsSelected = risk == RiskCategory.SafeToDelete
                        });
                        // Не заходим внутрь найденной папки
                    }
                    else if (!IsReparseDirectory(subDir))
                    {
                        // Не целевая и не reparse-точка (junction/symlink) — в стек
                        // для рекурсии. Спуск в reparse-точки запрещён (цикл-гвард,
                        // День 22): junction внутрь корня даёт бесконечный спуск и
                        // фантомные копии целевых папок, junction наружу — чужое
                        // содержимое; образец — SafeEnumerateDirectoriesRecursive (День 14)
                        stack.Push(subDir);
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { /* Пропускаем ошибки доступа */ System.Diagnostics.Debug.WriteLine($"[CustomFolderScanner] ScanPattern error: {ex.Message}"); }
    }

    /// <summary>
    /// Сканирует temp/tmp/log/bak файлы непосредственно в корневой папке.
    /// </summary>
    private void ScanTempFiles(string rootPath, List<ScannedItem> items, CancellationToken ct)
    {
        string[] tempExtensions = [".tmp", ".temp", ".log", ".bak", ".old"];
        long totalSize = 0;
        var tempFiles = new List<string>();

        try
        {
            foreach (var file in SafeEnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (tempExtensions.Contains(ext))
                {
                    tempFiles.Add(file);
                    totalSize += GetFileSize(file);
                }
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CustomFolderScanner] ScanTempFiles error: {ex.Message}"); }

        if (tempFiles.Count > 0 && totalSize > 0)
        {
            // Добавляем сгруппированно по файлам
            foreach (var file in tempFiles)
            {
                var size = GetFileSize(file);
                if (size > 0)
                {
                    items.Add(new ScannedItem
                    {
                        Path = file,
                        Size = size,
                        LastAccess = GetLastAccess(file),
                        Risk = RiskCategory.SafeToDelete,
                        Description = Localizer.GetString("Scanner_Custom_Desc_TempFile", Path.GetFileName(file)),
                        Explanation = Localizer.GetString("Scanner_Custom_Expl_TempFile"),
                        IsDirectory = false,
                        IsSelected = true
                    });
                }
            }
        }
    }
}
