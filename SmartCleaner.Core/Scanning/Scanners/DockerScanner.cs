using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер Docker-артефактов: dangling images, build cache, stopped containers.
/// Требует установленного Docker Desktop.
/// </summary>
public class DockerScanner : ScannerBase
{
    public override string CategoryName => "Docker";
    public override string CategoryIcon => "\uE839"; // Segoe MDL2: CloudDownload-like
    public override int DisplayOrder => 11;
    public override bool IsEnabledByDefault => false; // По умолчанию выключен — не у всех есть Docker

    public DockerScanner(IKnowledgeBase knowledge, ISafetyService safety)
        : base(knowledge, safety) { }

    public override async Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon
        };

        // Проверяем доступность Docker
        if (!await IsDockerAvailableAsync(ct))
        {
            System.Diagnostics.Trace.TraceInformation("[Docker] Docker CLI не доступен, пропускаем сканирование");
            return result;
        }

        progress?.Report("Анализ Docker...");

        // 1. Dangling images (без тегов)
        await ScanDanglingImagesAsync(result.Items, progress, ct);

        // 2. Build cache
        await ScanBuildCacheAsync(result.Items, progress, ct);

        // 3. Stopped containers
        await ScanStoppedContainersAsync(result.Items, progress, ct);

        return result;
    }

    /// <summary>
    /// Проверяет доступность docker CLI
    /// </summary>
    private static async Task<bool> IsDockerAvailableAsync(CancellationToken ct)
    {
        try
        {
            var (exitCode, _) = await RunDockerCommandAsync("version --format {{.Server.Version}}", ct);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Ищет dangling images (образы без тегов: &lt;none&gt;:&lt;none&gt;)
    /// </summary>
    private async Task ScanDanglingImagesAsync(IList<ScannedItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Docker: поиск dangling images...");

        var (exitCode, output) = await RunDockerCommandAsync("images -f dangling=true --format \"{{.ID}} {{.Size}}\"", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            var parts = line.Split(' ', 2);
            if (parts.Length < 1) continue;

            var imageId = parts[0];
            var sizeStr = parts.Length > 1 ? parts[1] : "unknown";
            var estimatedSize = ParseDockerSize(sizeStr);

            items.Add(new ScannedItem
            {
                Path = $"docker:image:{imageId}",
                Size = estimatedSize,
                Risk = RiskCategory.SafeToDelete,
                Description = $"Dangling Docker image {imageId[..12]} ({sizeStr}) — без тегов, можно безопасно удалить",
                IsDirectory = false,
                ParentApp = "Docker",
                IsSelected = true,
                RestoreCommand = null // Нельзя восстановить
            });
        }
    }

    /// <summary>
    /// Оценивает размер Docker build cache
    /// </summary>
    private async Task ScanBuildCacheAsync(IList<ScannedItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Docker: анализ build cache...");

        var (exitCode, output) = await RunDockerCommandAsync("system df --format \"{{.Type}} {{.Size}} {{.Reclaimable}}\"", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            var type = parts[0]; // Images, Containers, Local Volumes, Build Cache
            var reclaimableStr = parts[^1]; // e.g. "1.2GB (100%)"
            
            if (type == "Build" || (parts.Length > 1 && parts[1] == "Cache"))
            {
                var sizeStr = parts.Length > 2 ? parts[2] : parts[1];
                var estimatedSize = ParseDockerSize(sizeStr);
                
                if (estimatedSize > 10 * 1024 * 1024) // > 10MB
                {
                    items.Add(new ScannedItem
                    {
                        Path = "docker:build-cache",
                        Size = estimatedSize,
                        Risk = RiskCategory.PerformanceCache,
                        Description = $"Docker build cache ({sizeStr}) — безопасно очистить через 'docker builder prune'",
                        IsDirectory = false,
                        ParentApp = "Docker",
                        IsSelected = false,
                        RestoreCommand = "docker builder prune"
                    });
                }
            }
        }
    }

    /// <summary>
    /// Ищет остановленные контейнеры
    /// </summary>
    private async Task ScanStoppedContainersAsync(IList<ScannedItem> items, IProgress<string>? progress, CancellationToken ct)
    {
        progress?.Report("Docker: поиск остановленных контейнеров...");

        var (exitCode, output) = await RunDockerCommandAsync("ps -a -f status=exited --format \"{{.ID}} {{.Names}} {{.Size}}\"", ct);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
            return;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            var parts = line.Split(' ', 3);
            if (parts.Length < 2) continue;

            var containerId = parts[0];
            var containerName = parts[1];
            var sizeStr = parts.Length > 2 ? parts[2] : "unknown";
            var estimatedSize = ParseDockerSize(sizeStr);

            items.Add(new ScannedItem
            {
                Path = $"docker:container:{containerId}",
                Size = estimatedSize,
                Risk = RiskCategory.SafeToDelete,
                Description = $"Остановленный контейнер «{containerName}» — можно удалить через 'docker rm'",
                IsDirectory = false,
                ParentApp = "Docker",
                IsSelected = true,
                RestoreCommand = null
            });
        }
    }

    /// <summary>
    /// Запустить docker команду и получить вывод
    /// </summary>
    private static async Task<(int exitCode, string output)> RunDockerCommandAsync(string arguments, CancellationToken ct)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return (process.ExitCode, output);
    }

    /// <summary>
    /// Примерно парсить Docker размеры (1.2GB, 500MB, 2kB и т.п.)
    /// </summary>
    private static long ParseDockerSize(string sizeStr)
    {
        sizeStr = sizeStr.Trim().ToUpperInvariant();

        // Убираем скобки и проценты: "1.2GB (100%)" → "1.2GB"
        var parenIdx = sizeStr.IndexOf('(');
        if (parenIdx > 0) sizeStr = sizeStr[..parenIdx].Trim();

        double multiplier = 1;
        if (sizeStr.EndsWith("GB")) { multiplier = 1024.0 * 1024 * 1024; sizeStr = sizeStr[..^2]; }
        else if (sizeStr.EndsWith("MB")) { multiplier = 1024.0 * 1024; sizeStr = sizeStr[..^2]; }
        else if (sizeStr.EndsWith("KB")) { multiplier = 1024.0; sizeStr = sizeStr[..^2]; }
        else if (sizeStr.EndsWith("B")) { sizeStr = sizeStr[..^1]; }

        if (double.TryParse(sizeStr.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return (long)(value * multiplier);

        return 0;
    }
}
