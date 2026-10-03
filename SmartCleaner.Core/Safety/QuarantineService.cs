using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SmartCleaner.Core.Safety;

public class QuarantinedItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string OriginalPath { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = "0 B";
    public DateTime QuarantinedAt { get; set; } = DateTime.Now;
    public bool IsDirectory { get; set; }
    public string Category { get; set; } = "Мусор";
}

public class QuarantineManifest
{
    public List<QuarantinedItem> Items { get; set; } = new();

    /// <summary>
    /// Base64(HMAC-SHA256(per-install ключ, каноническая форма манифеста)).
    /// Схема подписи — <see cref="QuarantineManifestSigner"/>. Пустая строка —
    /// манифест формата до подписи (legacy).
    /// </summary>
    public string Signature { get; set; } = string.Empty;
}

public class QuarantineService
{
    private readonly string _quarantineDir;
    private readonly string _manifestFile;
    private readonly string _storageDir;
    private readonly IConfigService _configService;

    private readonly object _keyLock = new();
    private byte[]? _signingKey;

    /// <summary>
    /// Карантин в %LOCALAPPDATA%\SmartCleaner\Quarantine, ключ подписи —
    /// через реальный ConfigService (portable/installed режим).
    /// </summary>
    public QuarantineService()
        : this(GetDefaultQuarantineRoot(), new ConfigService())
    {
    }

    public QuarantineService(IConfigService configService)
        : this(GetDefaultQuarantineRoot(), configService)
    {
    }

    /// <summary>
    /// Конструктор с изолированным корнем карантина (для тестов: вместо %LOCALAPPDATA%)
    /// и явным источником ключа подписи манифеста.
    /// </summary>
    public QuarantineService(string quarantineRoot, IConfigService configService)
    {
        _quarantineDir = quarantineRoot;
        _storageDir = Path.Combine(_quarantineDir, "Storage");
        _manifestFile = Path.Combine(_quarantineDir, "manifest.json");
        _configService = configService;

        try
        {
            Directory.CreateDirectory(_storageDir);
        }
        catch (Exception ex)
        {
            // Если не удалось создать директорию — карантин недоступен
            Debug.WriteLine($"[Quarantine] Storage directory unavailable: {ex.Message}");
        }
    }

    private static string GetDefaultQuarantineRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "SmartCleaner", "Quarantine");
    }

    /// <summary>
    /// Per-install ключ HMAC из ConfigDirectory (IConfigService); создаётся при
    /// первом использовании. Схема подписи — <see cref="QuarantineManifestSigner"/>.
    /// </summary>
    private byte[] GetOrCreateSigningKey()
    {
        lock (_keyLock)
        {
            _signingKey ??= QuarantineManifestSigner.GetOrCreateSigningKey(_configService);
            return _signingKey;
        }
    }

    public async Task<List<QuarantinedItem>> GetQuarantinedItemsAsync()
    {
        if (!File.Exists(_manifestFile))
            return new List<QuarantinedItem>();

        try
        {
            var json = await File.ReadAllTextAsync(_manifestFile);
            var manifest = JsonSerializer.Deserialize<QuarantineManifest>(json);
            return manifest?.Items ?? new List<QuarantinedItem>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] Manifest read failed (treated as empty): {ex.Message}");
            return new List<QuarantinedItem>();
        }
    }

    public async Task<bool> MoveToQuarantineAsync(string path, string category = "Очистка")
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        var isDir = Directory.Exists(path);
        var name = Path.GetFileName(path);
        var id = Guid.NewGuid().ToString("N");
        var destFolder = Path.Combine(_storageDir, id);
        Directory.CreateDirectory(destFolder);
        var destPath = Path.Combine(destFolder, name);

        long size = 0;
        try
        {
            if (isDir)
            {
                size = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } });
                Directory.Move(path, destPath);
            }
            else
            {
                size = new FileInfo(path).Length;
                File.Move(path, destPath);
            }

            var item = new QuarantinedItem
            {
                Id = id,
                OriginalPath = path,
                StoredPath = destPath,
                Name = name,
                SizeBytes = size,
                SizeFormatted = SizeFormatter.Format(size),
                QuarantinedAt = DateTime.Now,
                IsDirectory = isDir,
                Category = category
            };

            var items = await GetQuarantinedItemsAsync();
            items.Add(item);
            await SaveManifestAsync(items);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] Move to quarantine failed for {path}: {ex.Message}");
            return false;
        }
    }

    public async Task<(bool Success, string Message)> RestoreItemAsync(string id)
    {
        var items = await GetQuarantinedItemsAsync();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null) return (false, "Элемент не найден в манифесте");

        if (!File.Exists(item.StoredPath) && !Directory.Exists(item.StoredPath))
            return (false, "Файл не найден в хранилище карантина");

        try
        {
            var targetDir = Path.GetDirectoryName(item.OriginalPath);
            if (!string.IsNullOrWhiteSpace(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            if (item.IsDirectory)
            {
                Directory.Move(item.StoredPath, item.OriginalPath);
            }
            else
            {
                File.Move(item.StoredPath, item.OriginalPath);
            }

            // Cleanup storage folder
            var folder = Path.GetDirectoryName(item.StoredPath);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            items.Remove(item);
            await SaveManifestAsync(items);

            return (true, $"Элемент '{item.Name}' успешно восстановлен в {item.OriginalPath}!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка восстановления: {ex.Message}");
        }
    }

    public async Task<bool> PurgeItemAsync(string id)
    {
        var items = await GetQuarantinedItemsAsync();
        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null) return false;

        try
        {
            var folder = Path.GetDirectoryName(item.StoredPath);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            items.Remove(item);
            await SaveManifestAsync(items);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] Purge failed for {id}: {ex.Message}");
            return false;
        }
    }

    private static readonly JsonSerializerOptions ManifestWriteOptions = new() { WriteIndented = true };

    private async Task SaveManifestAsync(List<QuarantinedItem> items)
    {
        var manifest = new QuarantineManifest { Items = items };
        // Каждая запись манифеста подписывается per-install ключом
        // (схема — QuarantineManifestSigner): user-writable manifest.json,
        // изменённый без доступа к ключу, будет отклонён при Restore/Purge.
        manifest.Signature = QuarantineManifestSigner.ComputeSignature(manifest, GetOrCreateSigningKey());
        var json = JsonSerializer.Serialize(manifest, ManifestWriteOptions);
        await File.WriteAllTextAsync(_manifestFile, json);
    }
}
