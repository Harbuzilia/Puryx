using System.IO;
using System.Text.Json;
using DotNet.Globbing;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Safety;

/// <summary>
/// Реализация сервиса безопасности
/// </summary>
public class SafetyService : ISafetyService
{
    private readonly IConfigService _config;
    private readonly List<string> _builtInPatterns = [];
    private readonly List<string> _userPatterns = [];
    
    private const string WhitelistFilename = "whitelist.json";

    public TimeSpan ProtectedPeriod { get; set; } = TimeSpan.FromHours(24);

    public SafetyService(IConfigService config)
    {
        _config = config;
        LoadWhitelist();
    }

    public bool IsWhitelisted(string path)
    {
        var normalizedPath = NormalizePath(path);
        
        // Проверяем все паттерны
        foreach (var pattern in _builtInPatterns.Concat(_userPatterns))
        {
            var expandedPattern = Environment.ExpandEnvironmentVariables(pattern);
            var normalizedPattern = NormalizePath(expandedPattern);
            
            try
            {
                var glob = Glob.Parse(normalizedPattern);
                if (glob.IsMatch(normalizedPath))
                    return true;
            }
            catch
            {
                // Если паттерн невалидный — простое сравнение
                if (normalizedPath.StartsWith(normalizedPattern.TrimEnd('*', '/')))
                    return true;
            }
        }
        
        return false;
    }

    public void AddToWhitelist(string pattern)
    {
        if (!_userPatterns.Contains(pattern))
        {
            _userPatterns.Add(pattern);
            SaveWhitelist();
        }
    }

    public void RemoveFromWhitelist(string pattern)
    {
        if (_userPatterns.Remove(pattern))
        {
            SaveWhitelist();
        }
    }

    public IEnumerable<string> GetWhitelistPatterns()
    {
        return _builtInPatterns.Concat(_userPatterns);
    }

    public bool IsWithinProtectedPeriod(string path)
    {
        try
        {
            DateTime lastWrite;
            
            if (Directory.Exists(path))
                lastWrite = Directory.GetLastWriteTime(path);
            else if (File.Exists(path))
                lastWrite = File.GetLastWriteTime(path);
            else
                return false;

            return DateTime.Now - lastWrite < ProtectedPeriod;
        }
        catch
        {
            return true; // При ошибке — считаем защищённым
        }
    }

    public bool IsFileLocked(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    public DeleteValidation ValidateForDeletion(ScannedItem item)
    {
        // 1. Проверяем whitelist
        if (IsWhitelisted(item.Path))
        {
            return new DeleteValidation
            {
                CanDelete = false,
                BlockReason = "Файл в белом списке защиты"
            };
        }

        // 2. Проверяем блокировку
        if (item.IsLocked || (!item.IsDirectory && IsFileLocked(item.Path)))
        {
            return new DeleteValidation
            {
                CanDelete = false,
                BlockReason = "Файл заблокирован другим процессом"
            };
        }

        // 3. Проверяем период защиты — только для пользовательских данных.
        // Кэши (SafeToDelete, PerformanceCache) обновляются постоянно, блокировка по времени бессмысленна.
        if (item.Risk == RiskCategory.UserData && IsWithinProtectedPeriod(item.Path))
        {
            return new DeleteValidation
            {
                CanDelete = false,
                BlockReason = $"Файл создан/изменён менее {ProtectedPeriod.TotalHours:0} часов назад"
            };
        }

        // 4. Проверяем необходимость elevation
        var requiresElevation = RequiresElevation(item.Path);

        return new DeleteValidation
        {
            CanDelete = true,
            RequiresElevation = requiresElevation
        };
    }

    public void SaveWhitelist()
    {
        var data = new WhitelistData
        {
            Version = "1.0",
            BuiltInPatterns = _builtInPatterns,
            UserPatterns = _userPatterns
        };
        
        _config.Save(WhitelistFilename, data);
    }

    public void LoadWhitelist()
    {
        // Загружаем встроенные паттерны из ресурсов
        _builtInPatterns.Clear();
        _builtInPatterns.AddRange(GetDefaultBuiltInPatterns());

        // Загружаем пользовательские паттерны
        _userPatterns.Clear();
        
        if (_config.Exists(WhitelistFilename))
        {
            var data = _config.Load<WhitelistData>(WhitelistFilename);
            if (data.UserPatterns != null)
            {
                _userPatterns.AddRange(data.UserPatterns);
            }
        }
    }

    /// <summary>
    /// Встроенные паттерны защиты
    /// </summary>
    private static List<string> GetDefaultBuiltInPatterns() =>
    [
        // AI Agents — критично!
        @"%USERPROFILE%\.gemini\**",
        @"%USERPROFILE%\.kiro\settings\**",
        @"%APPDATA%\Claude\claude_desktop_config.json",
        @"**\.codeium\windsurf\cascade\**",
        
        // IDE workspaces
        @"**\workspaceStorage\**",
        @"**\globalStorage\**",
        
        // Сохранения игр
        @"**\SaveGames\**",
        @"**\Saves\**",
        @"**\Save\**",
        @"**\SaveData\**",
        @"**\userdata\**",
        
        // Системы контроля версий
        @"**\.git\**",
        @"**\.svn\**",
        @"**\.hg\**"
        
        // УБРАНО: **\*.db, **\*.sqlite*, **\config\**, **\settings\**
        // Эти паттерны блокировали удаление легитимных кэшей (SQLite в браузерах,
        // config-файлы внутри npm cache). Защита конкретных приложений — через
        // KnowledgeBase.ProtectedPatterns для каждого приложения.
    ];

    /// <summary>
    /// Проверить, требуются ли права администратора
    /// </summary>
    private static bool RequiresElevation(string path)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        
        var normalizedPath = NormalizePath(path);
        
        return normalizedPath.StartsWith(NormalizePath(windowsDir)) ||
               normalizedPath.StartsWith(NormalizePath(programFiles)) ||
               normalizedPath.StartsWith(NormalizePath(programFilesX86));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
    }

    /// <summary>
    /// Модель данных whitelist
    /// </summary>
    private class WhitelistData
    {
        public string Version { get; set; } = "1.0";
        public List<string> BuiltInPatterns { get; set; } = [];
        public List<string> UserPatterns { get; set; } = [];
    }
}
