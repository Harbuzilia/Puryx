using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;
using System.Text.Json;

namespace SmartCleaner.Core.Knowledge;

/// <summary>
/// Реализация базы знаний
/// </summary>
public class KnowledgeBase : IKnowledgeBase
{
    private readonly IConfigService _config;
    private readonly List<AppDefinition> _builtInApps = [];
    private readonly List<AppDefinition> _userApps = [];
    
    private const string UserAppsFilename = "user_apps.json";

    public KnowledgeBase(IConfigService config)
    {
        _config = config;
        LoadBuiltInApps();
        LoadUserApps();
    }

    public IEnumerable<AppDefinition> GetBuiltInApps() => _builtInApps;
    
    public IEnumerable<AppDefinition> GetUserApps() => _userApps;
    
    public IEnumerable<AppDefinition> GetAllApps() => _builtInApps.Concat(_userApps);

    public void AddUserApp(AppDefinition app)
    {
        var newApp = app with { IsUserDefined = true };
        _userApps.Add(newApp);
        SaveUserRules();
    }

    public void UpdateUserApp(AppDefinition app)
    {
        var index = _userApps.FindIndex(a => a.Id == app.Id);
        if (index >= 0)
        {
            _userApps[index] = app with { IsUserDefined = true };
            SaveUserRules();
        }
    }

    public void RemoveUserApp(string appId)
    {
        _userApps.RemoveAll(a => a.Id == appId);
        SaveUserRules();
    }

    public IEnumerable<DiscoveredApp> DiscoverUnknownApps(string rootPath)
    {
        var discovered = new List<DiscoveredApp>();

        if (!Directory.Exists(rootPath))
            return discovered;

        try
        {
            foreach (var appDir in Directory.EnumerateDirectories(rootPath))
            {
                var appName = Path.GetFileName(appDir);
                
                // Пропускаем уже известные приложения
                if (GetAllApps().Any(a => a.RootPaths.Any(p => 
                    Environment.ExpandEnvironmentVariables(p).Equals(appDir, StringComparison.OrdinalIgnoreCase))))
                    continue;

                var suggestedCache = new List<string>();
                var suggestedProtected = new List<string>();

                // Эвристика: ищем типичные папки кэша
                foreach (var subDir in Directory.EnumerateDirectories(appDir))
                {
                    var subName = Path.GetFileName(subDir).ToLowerInvariant();
                    
                    if (IsCacheFolder(subName))
                        suggestedCache.Add(subName);
                    else if (IsProtectedFolder(subName))
                        suggestedProtected.Add(subName);
                }

                // Эвристика: ищем типичные защищённые файлы
                foreach (var file in Directory.EnumerateFiles(appDir))
                {
                    var fileName = Path.GetFileName(file).ToLowerInvariant();
                    
                    if (IsProtectedFile(fileName))
                        suggestedProtected.Add(fileName);
                }

                if (suggestedCache.Count > 0 || suggestedProtected.Count > 0)
                {
                    // Fix #12: лёгкий подсчёт размера — только файлы верхнего уровня
                    // + содержимое обнаруженных кэш-папок (без рекурсивного обхода всего дерева)
                    long totalSize = 0;
                    try
                    {
                        // Файлы в корне приложения
                        totalSize = new DirectoryInfo(appDir)
                            .EnumerateFiles()
                            .Sum(f => { try { return f.Length; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] File length error: {ex.Message}"); return 0L; } });

                        // Размер обнаруженных кэш-подпапок
                        foreach (var cacheFolder in suggestedCache)
                        {
                            var cachePath = Path.Combine(appDir, cacheFolder);
                            if (Directory.Exists(cachePath))
                            {
                                try
                                {
                                    totalSize += new DirectoryInfo(cachePath)
                                        .EnumerateFiles("*", SearchOption.AllDirectories)
                                        .Sum(f => { try { return f.Length; } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] Cache file length error: {ex.Message}"); return 0L; } });
                                }
                                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] Cache folder enumeration error: {ex.Message}"); }
                            }
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] App dir size calculation error: {ex.Message}"); }

                    discovered.Add(new DiscoveredApp
                    {
                        Path = appDir,
                        SuggestedName = appName,
                        SuggestedCacheFolders = suggestedCache,
                        SuggestedProtectedFiles = suggestedProtected,
                        TotalSize = totalSize
                    });
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return discovered;
    }

    public PathClassification ClassifyPath(string path)
    {
        var expandedPath = path.ToLowerInvariant();

        foreach (var app in GetAllApps())
        {
            foreach (var rootPath in app.RootPaths)
            {
                var expandedRoot = Environment.ExpandEnvironmentVariables(rootPath).ToLowerInvariant();
                
                if (!expandedPath.StartsWith(expandedRoot))
                    continue;

                var relativePath = expandedPath[expandedRoot.Length..].TrimStart('\\', '/');

                // Проверяем защищённые паттерны
                foreach (var pattern in app.ProtectedPatterns)
                {
                    if (MatchesPattern(relativePath, pattern.ToLowerInvariant()))
                    {
                        return new PathClassification
                        {
                            IsKnownApp = true,
                            AppName = app.DisplayName,
                            SuggestedRisk = RiskCategory.UserData,
                            Reason = $"Совпадает с защищённым паттерном: {pattern}"
                        };
                    }
                }

                // Проверяем паттерны кэша
                foreach (var pattern in app.CachePatterns)
                {
                    if (MatchesPattern(relativePath, pattern.ToLowerInvariant()))
                    {
                        return new PathClassification
                        {
                            IsKnownApp = true,
                            AppName = app.DisplayName,
                            SuggestedRisk = RiskCategory.PerformanceCache,
                            Reason = $"Совпадает с паттерном кэша: {pattern}"
                        };
                    }
                }

                return new PathClassification
                {
                    IsKnownApp = true,
                    AppName = app.DisplayName,
                    SuggestedRisk = RiskCategory.PerformanceCache,
                    Reason = "Известное приложение, но путь не соответствует паттернам"
                };
            }
        }

        // Эвристика для неизвестных путей
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var dirName = Path.GetFileName(Path.GetDirectoryName(path) ?? "").ToLowerInvariant();

        if (IsProtectedFile(fileName) || IsProtectedFolder(dirName))
        {
            return new PathClassification
            {
                IsKnownApp = false,
                SuggestedRisk = RiskCategory.UserData,
                Reason = "Эвристика: файл/папка выглядит как пользовательские данные"
            };
        }

        if (IsCacheFolder(dirName) || IsTempFile(fileName))
        {
            return new PathClassification
            {
                IsKnownApp = false,
                SuggestedRisk = RiskCategory.SafeToDelete,
                Reason = "Эвристика: выглядит как кэш или временный файл"
            };
        }

        return new PathClassification
        {
            IsKnownApp = false,
            SuggestedRisk = RiskCategory.PerformanceCache,
            Reason = "Неизвестный путь"
        };
    }

    public void LoadExternalRules(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return;

        try
        {
            var json = File.ReadAllText(jsonPath);
            var apps = JsonSerializer.Deserialize<List<AppDefinition>>(json);
            
            if (apps != null)
            {
                foreach (var app in apps)
                {
                    if (!_userApps.Any(a => a.Id == app.Id))
                    {
                        _userApps.Add(app with { IsUserDefined = true });
                    }
                }
                SaveUserRules();
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[KnowledgeBase] LoadExternalRules error: {ex.Message}"); }
    }

    public void SaveUserRules()
    {
        _config.Save(UserAppsFilename, _userApps);
    }

    private void LoadBuiltInApps()
    {
        _builtInApps.Clear();
        _builtInApps.AddRange(GetDefaultBuiltInApps());
    }

    private void LoadUserApps()
    {
        _userApps.Clear();
        
        if (_config.Exists(UserAppsFilename))
        {
            var apps = _config.Load<List<AppDefinition>>(UserAppsFilename);
            _userApps.AddRange(apps);
        }
    }

    private static bool IsCacheFolder(string name) => name switch
    {
        "cache" or "caches" or "cached" or "cacheddata" or "cachedextensions" => true,
        "code cache" or "gpucache" or "shadercache" or "grshadercache" => true,
        "temp" or "tmp" or "temporary" => true,
        "logs" or "log" => true,
        _ => false
    };

    private static bool IsProtectedFolder(string name) => name switch
    {
        "config" or "configs" or "configuration" => true,
        "settings" or "preferences" => true,
        "data" or "userdata" or "user data" => true,
        "storage" or "localstorage" or "local storage" => true,
        "workspacestorage" or "globalstorage" => true,
        "savegames" or "saves" or "save" => true,
        "profile" or "profiles" => true,
        _ => false
    };

    private static bool IsProtectedFile(string name) => 
        name.EndsWith(".db") || 
        name.EndsWith(".sqlite") || 
        name.EndsWith(".sqlite3") ||
        name.Contains("config") ||
        name.Contains("settings") ||
        name.Contains("preferences");

    private static bool IsTempFile(string name) =>
        name.EndsWith(".tmp") ||
        name.EndsWith(".temp") ||
        name.EndsWith(".log") ||
        name.EndsWith(".bak") ||
        name.StartsWith("~");

    private static bool MatchesPattern(string path, string pattern)
    {
        // Простое сопоставление паттернов
        if (pattern.EndsWith("**"))
        {
            var prefix = pattern[..^2].TrimEnd('/');
            return path.StartsWith(prefix);
        }
        
        if (pattern.StartsWith("**/"))
        {
            var suffix = pattern[3..];
            return path.Contains(suffix);
        }

        if (pattern.Contains("*"))
        {
            var parts = pattern.Split('*');
            var index = 0;
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                var found = path.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0) return false;
                index = found + part.Length;
            }
            return true;
        }

        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(pattern + "\\") ||
               path.StartsWith(pattern + "/");
    }

    /// <summary>
    /// Встроенные определения приложений
    /// </summary>
    private static List<AppDefinition> GetDefaultBuiltInApps() =>
    [
        // AI Agents - полностью защищённые
        new AppDefinition
        {
            Id = "antigravity",
            DisplayName = "Antigravity (Google)",
            RootPaths = [@"%USERPROFILE%\.gemini"],
            CachePatterns = [],
            ProtectedPatterns = ["**"],
            Description = "Критичные данные AI-агента — НЕ УДАЛЯТЬ"
        },
        new AppDefinition
        {
            Id = "kiro",
            DisplayName = "Kiro (Amazon)",
            RootPaths = [@"%USERPROFILE%\.kiro"],
            CachePatterns = [],
            ProtectedPatterns = ["settings/**", "**/*.json"],
            Description = "Настройки и данные Kiro"
        },
        
        // AI Agents - с кэшем
        new AppDefinition
        {
            Id = "cursor",
            DisplayName = "Cursor AI",
            RootPaths = [@"%APPDATA%\Cursor"],
            CachePatterns = ["Cache", "CachedData", "Code Cache", "GPUCache", "logs"],
            ProtectedPatterns = ["User/globalStorage", "User/workspaceStorage", "User/settings.json"]
        },
        new AppDefinition
        {
            Id = "windsurf",
            DisplayName = "Windsurf (Codeium)",
            RootPaths = [@"%USERPROFILE%\.codeium"],
            CachePatterns = ["cache"],
            ProtectedPatterns = ["windsurf/cascade"],
            Description = "cascade содержит историю чатов!"
        },
        new AppDefinition
        {
            Id = "claude",
            DisplayName = "Claude Desktop",
            RootPaths = [@"%APPDATA%\Claude"],
            CachePatterns = ["logs"],
            ProtectedPatterns = ["claude_desktop_config.json", "*.db"]
        },
        
        // IDEs
        new AppDefinition
        {
            Id = "vscode",
            DisplayName = "Visual Studio Code",
            RootPaths = [@"%APPDATA%\Code"],
            CachePatterns = ["Cache", "CachedData", "CachedExtensions", "Code Cache", "GPUCache", "logs"],
            ProtectedPatterns = ["User/globalStorage", "User/workspaceStorage", "extensions"]
        }
    ];
}
