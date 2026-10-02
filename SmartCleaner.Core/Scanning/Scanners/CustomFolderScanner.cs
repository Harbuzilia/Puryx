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
            progress?.Report($"Сканирование: {rootPath}");

            // 1. node_modules
            ScanPattern(rootPath, "node_modules", items, ct,
                RiskCategory.SafeToDelete,
                "node_modules — зависимости Node.js проекта",
                "Зависимости npm/yarn/pnpm. Восстанавливаются из package.json",
                "Содержимое восстановится командой npm install",
                "npm install");

            // 2. __pycache__
            ScanPattern(rootPath, "__pycache__", items, ct,
                RiskCategory.SafeToDelete,
                "__pycache__ — скомпилированные .pyc файлы Python",
                "Python кэш байткода. Пересоздаётся автоматически при запуске",
                "Создаётся автоматически при следующем запуске Python");

            // 3. .pytest_cache
            ScanPattern(rootPath, ".pytest_cache", items, ct,
                RiskCategory.SafeToDelete,
                ".pytest_cache — кэш тестов pytest",
                "Кэш результатов тестирования. Безопасен для удаления",
                "Создаётся автоматически при запуске pytest");

            // 4. bin/obj (.NET artifacts)
            ScanPattern(rootPath, "bin", items, ct,
                RiskCategory.SafeToDelete,
                "bin — скомпилированные .NET артефакты",
                "Результат сборки .NET проекта. Пересоздаётся при dotnet build",
                "Пересоберётся при dotnet build / Visual Studio Build",
                "dotnet build",
                requireSibling: "*.csproj");

            ScanPattern(rootPath, "obj", items, ct,
                RiskCategory.SafeToDelete,
                "obj — промежуточные .NET артефакты",
                "Промежуточные файлы сборки. Безопасны для удаления",
                "Пересоздаются при dotnet build",
                "dotnet build",
                requireSibling: "*.csproj");

            // 5. dist / build output
            ScanPattern(rootPath, "dist", items, ct,
                RiskCategory.SafeToDelete,
                "dist — папка сборки проекта",
                "Скомпилированный вывод фронтенда/бэкенда",
                "Пересоберётся командой npm run build / vite build",
                requireSibling: "package.json");

            ScanPattern(rootPath, "build", items, ct,
                RiskCategory.SafeToDelete,
                "build — папка сборки проекта",
                "Результат сборки проекта",
                "Пересоберётся при следующей сборке",
                requireSibling: "package.json");

            // 6. .cache
            ScanPattern(rootPath, ".cache", items, ct,
                RiskCategory.SafeToDelete,
                ".cache — папка кэша инструментов",
                "Общий кэш сборщиков (Parcel, Babel, ESLint и др.)",
                "Создаётся автоматически при запуске инструмента");

            // 7. .venv / venv / env
            ScanPattern(rootPath, ".venv", items, ct,
                RiskCategory.PerformanceCache,
                ".venv — виртуальное окружение Python",
                "Python virtual environment. Содержит установленные зависимости",
                "Воссоздаётся: python -m venv .venv && pip install -r requirements.txt",
                "python -m venv .venv && pip install -r requirements.txt");

            ScanPattern(rootPath, "venv", items, ct,
                RiskCategory.PerformanceCache,
                "venv — виртуальное окружение Python",
                "Python virtual environment",
                "Воссоздаётся: python -m venv venv",
                "python -m venv venv");

            // 8. Temp/tmp файлы в корне
            ScanTempFiles(rootPath, items, ct);

            // 9. .next (Next.js cache)
            ScanPattern(rootPath, ".next", items, ct,
                RiskCategory.SafeToDelete,
                ".next — кэш сборки Next.js",
                "Кэш сборки и HMR Next.js фреймворка",
                "Пересоздаётся при npm run dev / npm run build",
                "npm run build",
                requireSibling: "next.config.*");

            // 10. .nuxt (Nuxt.js cache)
            ScanPattern(rootPath, ".nuxt", items, ct,
                RiskCategory.SafeToDelete,
                ".nuxt — кэш сборки Nuxt.js",
                "Кэш сборки Nuxt.js фреймворка",
                "Пересоздаётся при npm run dev",
                "npm run dev",
                requireSibling: "nuxt.config.*");

            // 11. coverage
            ScanPattern(rootPath, "coverage", items, ct,
                RiskCategory.SafeToDelete,
                "coverage — отчёты покрытия тестами",
                "Сгенерированные отчёты coverage. Безопасны для удаления",
                "Пересоздаются при запуске тестов с --coverage");

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
    /// <param name="description">Описание</param>
    /// <param name="explanation">Что это и зачем</param>
    /// <param name="howToRestore">Как восстановить</param>
    /// <param name="restoreCommand">Команда восстановления</param>
    /// <param name="requireSibling">Паттерн файла-соседа (*.csproj, package.json)</param>
    private void ScanPattern(
        string rootPath, string folderName, List<ScannedItem> items,
        CancellationToken ct, RiskCategory risk, string description,
        string? explanation = null, string? howToRestore = null,
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
                            Description = description,
                            Explanation = explanation,
                            HowToRestore = howToRestore,
                            RestoreCommand = restoreCommand,
                            IsDirectory = true,
                            IsSelected = risk == RiskCategory.SafeToDelete
                        });
                        // Не заходим внутрь найденной папки
                    }
                    else
                    {
                        // Не целевая — добавляем в стек для рекурсии
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
                        Description = $"Временный файл: {Path.GetFileName(file)}",
                        Explanation = "Временный/резервный файл. Безопасен для удаления",
                        IsDirectory = false,
                        IsSelected = true
                    });
                }
            }
        }
    }
}
