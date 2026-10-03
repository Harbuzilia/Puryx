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

    /// <summary>Состояние доверия манифеста карантина при чтении.</summary>
    private enum ManifestTrust
    {
        /// <summary>Файла манифеста нет — карантин пуст.</summary>
        Missing,
        /// <summary>Подпись корректна — операции над элементами разрешены.</summary>
        Valid,
        /// <summary>Signature отсутствует: формат до подписи (legacy) или crafted-манифест без подписи.</summary>
        UnsignedLegacy,
        /// <summary>Signature не сходится (манифест изменён) либо ключ подписи недоступен.</summary>
        InvalidSignature,
        /// <summary>Манифест не читается / не парсится.</summary>
        Corrupt
    }

    private static readonly JsonSerializerOptions ManifestReadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Читает манифест и классифицирует доверие. Элементы возвращаются даже для
    /// недоверенного манифеста — только для отображения (пользователь видит, что
    /// лежит в карантине, включая legacy); операции Restore/Purge требуют
    /// <see cref="ManifestTrust.Valid"/> (см. <see cref="RefusalForTrust"/>).
    /// </summary>
    private async Task<(ManifestTrust Trust, List<QuarantinedItem> Items)> ReadManifestWithTrustAsync()
    {
        if (!File.Exists(_manifestFile))
        {
            return (ManifestTrust.Missing, new List<QuarantinedItem>());
        }

        try
        {
            var json = await File.ReadAllTextAsync(_manifestFile);
            var manifest = JsonSerializer.Deserialize<QuarantineManifest>(json, ManifestReadOptions);
            if (manifest is null)
            {
                return (ManifestTrust.Corrupt, new List<QuarantinedItem>());
            }

            // Null-элементы (атакующий мог вписать "Items":[null]) отбрасываем
            var items = manifest.Items?
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList() ?? new List<QuarantinedItem>();

            if (string.IsNullOrEmpty(manifest.Signature))
            {
                return (ManifestTrust.UnsignedLegacy, items);
            }

            byte[]? key;
            try
            {
                key = GetOrCreateSigningKey();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Quarantine] Signing key unavailable: {ex.Message}");
                key = null;
            }

            if (key is null || !QuarantineManifestSigner.ValidateSignature(manifest, key))
            {
                return (ManifestTrust.InvalidSignature, items);
            }

            return (ManifestTrust.Valid, items);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] Manifest read failed (treated as corrupt): {ex.Message}");
            return (ManifestTrust.Corrupt, new List<QuarantinedItem>());
        }
    }

    /// <summary>
    /// Политика отказа Restore/Purge по состоянию доверия манифеста: пути из
    /// user-writable manifest.json исполняются только при валидной подписи (findings M6).
    /// Манифесты старого формата (без подписи) НЕ исполняются молча — восстановление
    /// вручную из папки карантина (ROADMAP День 12).
    /// </summary>
    private string? RefusalForTrust(ManifestTrust trust)
    {
        switch (trust)
        {
            case ManifestTrust.UnsignedLegacy:
                return "Манифест карантина устаревшего формата (без подписи); " +
                       $"восстановите файлы вручную из папки карантина: {_storageDir}";
            case ManifestTrust.InvalidSignature:
                return "Подпись манифеста карантина недействительна (манифест изменён " +
                       "или ключ подписи недоступен). Операция отклонена из соображений безопасности.";
            case ManifestTrust.Corrupt:
                return "Манифест карантина повреждён и не может быть прочитан. " +
                       "Операция отклонена из соображений безопасности.";
            default:
                return null; // Missing/Valid — операции разрешены (элемент может не найтись)
        }
    }

    /// <summary>
    /// Defence-in-depth: StoredPath обязан находиться строго под Storage карантина
    /// — даже при валидной подписи. Иначе Restore/Purge отклоняются ( crafted-путь
    /// не может указать рекурсивное удаление/перемещение вне карантина).
    /// Сравнение лексическое (junction-атаки — отдельная работа, ROADMAP День 13-14).
    /// </summary>
    private bool IsStoredPathWithinStorage(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        try
        {
            var storageRoot = Path.GetFullPath(_storageDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(storedPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            // Строго ниже корня Storage: совпадение с корнем и sibling-префиксы отвергаются
            return full.Length > storageRoot.Length
                && full.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] StoredPath validation failed for {storedPath}: {ex.Message}");
            return false;
        }
    }

    public async Task<List<QuarantinedItem>> GetQuarantinedItemsAsync()
    {
        var (_, items) = await ReadManifestWithTrustAsync();
        return items;
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

            // В новый подписанный манифест переносятся ТОЛЬКО элементы из валидно
            // подписанного манифеста: crafted/legacy записи не «отмываются» подписью
            // при следующей записи (fail-closed). Legacy-записи при этом пропадают из
            // списка (файлы остаются в Storage, восстановление — вручную).
            var (trust, existingItems) = await ReadManifestWithTrustAsync();
            if (trust is ManifestTrust.UnsignedLegacy or ManifestTrust.InvalidSignature)
            {
                Debug.WriteLine($"[Quarantine] Manifest trust={trust}: existing entries dropped from new signed manifest");
            }
            var items = trust == ManifestTrust.Valid
                ? existingItems
                : new List<QuarantinedItem>();
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
        var (trust, items) = await ReadManifestWithTrustAsync();

        // OriginalPath/StoredPath исполняются только из подписанного манифеста:
        // crafted manifest.json не должен перемещать файлы по произвольным путям
        var refusal = RefusalForTrust(trust);
        if (refusal is not null)
        {
            return (false, refusal);
        }

        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null) return (false, "Элемент не найден в манифесте");

        if (!IsStoredPathWithinStorage(item.StoredPath))
        {
            return (false, "Путь хранения элемента находится вне каталога карантина — " +
                           "операция отклонена из соображений безопасности.");
        }

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

    /// <summary>
    /// Удаляет элемент из карантина навсегда. Рекурсивно удаляет только каталог
    /// внутри Storage карантина; манифест обязан иметь валидную подпись.
    /// </summary>
    public async Task<(bool Success, string Message)> PurgeItemAsync(string id)
    {
        var (trust, items) = await ReadManifestWithTrustAsync();

        // Рекурсивный Directory.Delete исполняется только из подписанного манифеста:
        // crafted StoredPath не должен удалять произвольные каталоги (findings M6)
        var refusal = RefusalForTrust(trust);
        if (refusal is not null)
        {
            return (false, refusal);
        }

        var item = items.FirstOrDefault(i => i.Id == id);
        if (item == null) return (false, "Элемент не найден в манифесте");

        if (!IsStoredPathWithinStorage(item.StoredPath))
        {
            return (false, "Путь хранения элемента находится вне каталога карантина — " +
                           "операция отклонена из соображений безопасности.");
        }

        try
        {
            var folder = Path.GetDirectoryName(item.StoredPath);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);

            items.Remove(item);
            await SaveManifestAsync(items);
            return (true, $"Элемент '{item.Name}' удалён из карантина без возможности восстановления.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Quarantine] Purge failed for {id}: {ex.Message}");
            return (false, $"Ошибка удаления: {ex.Message}");
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
