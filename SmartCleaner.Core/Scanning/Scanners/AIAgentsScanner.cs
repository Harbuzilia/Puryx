using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер AI-агентов и IDE
/// </summary>
public class AIAgentsScanner : ScannerBase
{
    public override string CategoryName => "AI-агенты";
    public override string CategoryIcon => "\uE99A"; // Segoe MDL2: Robot
    public override int DisplayOrder => 4;

    // AI-агенты и IDE с их путями
    private static readonly Dictionary<string, AgentInfo> Agents = new()
    {
        ["Cursor"] = new AgentInfo
        {
            DisplayName = "Cursor AI",
            RootPaths = [
                @"%APPDATA%\Cursor"
            ],
            CacheFolders = ["Cache", "CachedData", "Code Cache", "GPUCache", "logs"],
            ProtectedFolders = ["User/globalStorage", "User/workspaceStorage"],
            ProtectedFiles = ["User/settings.json", "User/keybindings.json"]
        },
        ["VSCode"] = new AgentInfo
        {
            DisplayName = "Visual Studio Code",
            RootPaths = [
                @"%APPDATA%\Code"
            ],
            CacheFolders = ["Cache", "CachedData", "CachedExtensions", "CachedExtensionVSIXs", "Code Cache", "GPUCache", "logs"],
            ProtectedFolders = ["User/globalStorage", "User/workspaceStorage", "extensions"],
            ProtectedFiles = ["User/settings.json", "User/keybindings.json"]
        },
        ["Windsurf"] = new AgentInfo
        {
            DisplayName = "Windsurf (Codeium)",
            RootPaths = [
                @"%USERPROFILE%\.codeium"
            ],
            CacheFolders = ["cache"],
            ProtectedFolders = ["windsurf/cascade"], // История чатов!
            ProtectedFiles = []
        },
        ["ClaudeDesktop"] = new AgentInfo
        {
            DisplayName = "Claude Desktop",
            RootPaths = [
                @"%APPDATA%\Claude"
            ],
            CacheFolders = ["logs"],
            ProtectedFolders = [],
            ProtectedFiles = ["claude_desktop_config.json"]
        }
    };

    // Полностью защищённые агенты (НЕ сканируем кэш!)
    private static readonly Dictionary<string, string[]> ProtectedAgents = new()
    {
        ["Antigravity"] = [@"%USERPROFILE%\.gemini"],
        ["Kiro"] = [@"%USERPROFILE%\.kiro"]
    };

    public AIAgentsScanner(ISafetyService safety) 
        : base(safety)
    {
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            // Сканируем агентов с кэшем
            foreach (var (key, agent) in Agents)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Сканирование {agent.DisplayName}...");
                ScanAgent(items, agent, ct);
            }

            // Показываем защищённых агентов (информационно)
            foreach (var (name, paths) in ProtectedAgents)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Проверка {name}...");
                ShowProtectedAgent(items, name, paths);
            }

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    private void ScanAgent(List<ScannedItem> items, AgentInfo agent, CancellationToken ct)
    {
        foreach (var rootPathTemplate in agent.RootPaths)
        {
            var rootPath = ExpandPath(rootPathTemplate);
            if (!Directory.Exists(rootPath)) continue;

            try
            {
                // Сканируем папки кэша
                foreach (var cacheFolder in agent.CacheFolders)
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var cachePath = Path.Combine(rootPath, cacheFolder);
                    if (Directory.Exists(cachePath))
                    {
                        var size = CalculateDirectorySize(cachePath);
                        if (size > 1024 * 100) // > 100 KB
                        {
                            var description = cacheFolder.ToLowerInvariant() switch
                            {
                                "cache" => "Кэш приложения",
                                "cacheddata" => "Кэшированные данные",
                                "code cache" => "Кэш JavaScript кода",
                                "gpucache" => "Кэш GPU",
                                "logs" => "Файлы логов",
                                _ => $"Кэш: {cacheFolder}"
                            };

                            items.Add(new ScannedItem
                            {
                                Path = cachePath,
                                Size = size,
                                LastAccess = GetLastAccess(cachePath),
                                Risk = cacheFolder.ToLowerInvariant() == "logs" 
                                    ? RiskCategory.SafeToDelete 
                                    : RiskCategory.PerformanceCache,
                                Description = description,
                                IsDirectory = true,
                                ParentApp = agent.DisplayName,
                                IsSelected = cacheFolder.ToLowerInvariant() == "logs"
                            });
                        }
                    }
                }

                // Показываем защищённые папки (информационно, с красным индикатором)
                foreach (var protectedFolder in agent.ProtectedFolders)
                {
                    var protectedPath = Path.Combine(rootPath, protectedFolder);
                    if (Directory.Exists(protectedPath))
                    {
                        var size = CalculateDirectorySize(protectedPath);
                        if (size > 0)
                        {
                            items.Add(new ScannedItem
                            {
                                Path = protectedPath,
                                Size = size,
                                LastAccess = GetLastAccess(protectedPath),
                                Risk = RiskCategory.UserData,
                                Description = "⚠️ ЗАЩИЩЕНО — содержит пользовательские данные",
                                IsDirectory = true,
                                ParentApp = agent.DisplayName,
                                IsSelected = false
                            });
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private void ShowProtectedAgent(List<ScannedItem> items, string agentName, string[] paths)
    {
        foreach (var pathTemplate in paths)
        {
            var path = ExpandPath(pathTemplate);
            if (!Directory.Exists(path)) continue;

            try
            {
                var size = CalculateDirectorySize(path);
                if (size > 0)
                {
                    items.Add(new ScannedItem
                    {
                        Path = path,
                        Size = size,
                        LastAccess = GetLastAccess(path),
                        Risk = RiskCategory.UserData,
                        Description = $"🔒 ПОЛНОСТЬЮ ЗАЩИЩЕНО — {agentName} хранит критичные данные",
                        IsDirectory = true,
                        ParentApp = agentName,
                        IsSelected = false
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    private record AgentInfo
    {
        public required string DisplayName { get; init; }
        public required string[] RootPaths { get; init; }
        public required string[] CacheFolders { get; init; }
        public required string[] ProtectedFolders { get; init; }
        public required string[] ProtectedFiles { get; init; }
    }
}
