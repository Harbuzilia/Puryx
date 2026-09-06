using System.Text.Json;

namespace SmartCleaner.Core.Services;

/// <summary>
/// Реализация сервиса конфигурации
/// </summary>
public class ConfigService : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ConfigMode Mode { get; }
    public string ConfigDirectory { get; }
    public string LogDirectory { get; }
    public string UserRulesDirectory { get; }

    public ConfigService()
    {
        // Определяем режим по наличию файла-маркера или переменной окружения
        Mode = DetermineConfigMode();
        
        if (Mode == ConfigMode.Portable)
        {
            var exeDir = AppContext.BaseDirectory;
            ConfigDirectory = Path.Combine(exeDir, "config");
            LogDirectory = Path.Combine(exeDir, "logs");
            UserRulesDirectory = Path.Combine(exeDir, "rules");
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var baseDir = Path.Combine(appData, "SmartCleaner");
            ConfigDirectory = Path.Combine(baseDir, "config");
            LogDirectory = Path.Combine(baseDir, "logs");
            UserRulesDirectory = Path.Combine(baseDir, "rules");
        }

        // Создаём директории если не существуют
        EnsureDirectoryExists(ConfigDirectory);
        EnsureDirectoryExists(LogDirectory);
        EnsureDirectoryExists(UserRulesDirectory);
    }

    /// <summary>
    /// Определить режим конфигурации
    /// </summary>
    private static ConfigMode DetermineConfigMode()
    {
        // 1. Проверяем переменную окружения (устанавливается при сборке)
        var envMode = Environment.GetEnvironmentVariable("SMARTCLEANER_MODE");
        if (!string.IsNullOrEmpty(envMode) && envMode.Equals("installed", StringComparison.OrdinalIgnoreCase))
            return ConfigMode.Installed;

        // 2. Проверяем файл-маркер portable.txt рядом с exe
        var portableMarker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        if (File.Exists(portableMarker))
            return ConfigMode.Portable;

        // 3. Проверяем, есть ли уже папка config рядом с exe
        var portableConfig = Path.Combine(AppContext.BaseDirectory, "config");
        if (Directory.Exists(portableConfig))
            return ConfigMode.Portable;

        // 4. По умолчанию — Portable (для разработки)
        return ConfigMode.Portable;
    }

    public T Load<T>(string filename) where T : new()
    {
        var path = Path.Combine(ConfigDirectory, filename);
        
        if (!File.Exists(path))
            return new T();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? new T();
        }
        catch
        {
            return new T();
        }
    }

    public void Save<T>(string filename, T data)
    {
        var path = Path.Combine(ConfigDirectory, filename);
        
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не падаем
            System.Diagnostics.Debug.WriteLine($"Failed to save config {filename}: {ex.Message}");
        }
    }

    public bool Exists(string filename)
    {
        var path = Path.Combine(ConfigDirectory, filename);
        return File.Exists(path);
    }

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch
            {
                // Игнорируем ошибки создания директорий
            }
        }
    }
}
