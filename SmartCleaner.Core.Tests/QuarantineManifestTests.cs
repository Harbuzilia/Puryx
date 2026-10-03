using SmartCleaner.Core.Safety;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 12 — M6 (ROADMAP, findings M6): manifest.json лежит в user-writable каталоге
/// карантина и не подписан. Тесты фиксируют целевое поведение: подделанный (crafted)
/// манифест не должен позволять Restore/Purge перемещать или рекурсивно удалять
/// каталоги вне карантина; манифест, записанный самим сервисом, подписан и проходит.
/// </summary>
public class QuarantineManifestTests : IDisposable
{
    private readonly string _root;
    private readonly string _storageDir;
    private readonly string _manifestFile;
    private readonly string _configDirectory;
    private readonly FileBackedConfigService _config;

    /// <summary>Известный тесту per-install ключ: сервис загрузит его из ConfigDirectory.</summary>
    private readonly byte[] _key;

    public QuarantineManifestTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"quarantine_manifest_{Guid.NewGuid():N}");
        _storageDir = Path.Combine(_root, "Storage");
        _manifestFile = Path.Combine(_root, "manifest.json");
        Directory.CreateDirectory(_storageDir);

        _configDirectory = Path.Combine(Path.GetTempPath(), $"quarantine_manifest_config_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_configDirectory);
        _key = RandomNumberGenerator.GetBytes(32);
        _config = new FileBackedConfigService(_configDirectory);
        _config.Save(QuarantineManifestSigner.KeyFileName, new QuarantineSigningKey
        {
            KeyBase64 = Convert.ToBase64String(_key)
        });
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_configDirectory, recursive: true); } catch { }
    }

    private QuarantineService CreateService() => new(_root, _config);

    /// <summary>Создаёт файл внутри Storage карантина — «настоящее» хранилище для crafted-манифеста.</summary>
    private string CreateStoredFile(string id, string name)
    {
        var dir = Path.Combine(_storageDir, id);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "stored content");
        return path;
    }

    private static QuarantinedItem MakeItem(string id, string storedPath, string originalPath, bool isDirectory = false) => new()
    {
        Id = id,
        OriginalPath = originalPath,
        StoredPath = storedPath,
        Name = Path.GetFileName(originalPath),
        SizeBytes = 1,
        QuarantinedAt = new DateTime(2026, 1, 2, 3, 4, 5),
        IsDirectory = isDirectory
    };

    /// <summary>
    /// Пишет manifest.json «руками злоумышленника»: чистый JSON в формате сервиса,
    /// поле Signature — опционально (как будто манифест подменили после записи).
    /// </summary>
    private void WriteCraftedManifest(QuarantinedItem item, string? signature = null)
    {
        var itemJson = JsonSerializer.Serialize(item);
        var json = signature is null
            ? $$"""{"Items":[{{itemJson}}]}"""
            : $$"""{"Items":[{{itemJson}}],"Signature":"{{signature}}"}""";
        File.WriteAllText(_manifestFile, json);
    }

    private (string Dir, string File) CreateVictim()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"quarantine_victim_{Guid.NewGuid():N}");
        var file = Path.Combine(dir, "important.txt");
        Directory.CreateDirectory(dir);
        File.WriteAllText(file, "victim data");
        return (dir, file);
    }

    private QuarantineManifest ReadManifestFromDisk()
    {
        var json = File.ReadAllText(_manifestFile);
        return JsonSerializer.Deserialize<QuarantineManifest>(json) ?? new QuarantineManifest();
    }

    [Fact]
    public async Task MoveToQuarantine_WritesManifestSignedWithInstallKey()
    {
        var service = CreateService();
        var source = Path.Combine(Path.GetTempPath(), $"quarantine_sign_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(source, "sign me");

        try
        {
            Assert.True(await service.MoveToQuarantineAsync(source, "TestCategory"));

            var manifest = ReadManifestFromDisk();
            Assert.False(string.IsNullOrWhiteSpace(manifest.Signature));
            Assert.True(QuarantineManifestSigner.ValidateSignature(manifest, _key),
                "Манифест должен быть подписан per-install ключом из ConfigDirectory");
        }
        finally
        {
            try { File.Delete(source); } catch { }
        }
    }

    [Fact]
    public async Task Move_RestoreAndPurge_ThroughOwnManifest_Works()
    {
        var service = CreateService();
        var source = Path.Combine(Path.GetTempPath(), $"quarantine_roundtrip_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(source, "roundtrip content");

        try
        {
            // Move -> Restore
            Assert.True(await service.MoveToQuarantineAsync(source, "TestCategory"));
            Assert.False(File.Exists(source));

            var items = await service.GetQuarantinedItemsAsync();
            var item = Assert.Single(items);
            Assert.Equal(source, item.OriginalPath);
            Assert.True(
                Path.GetFullPath(item.StoredPath).StartsWith(Path.GetFullPath(_storageDir), StringComparison.OrdinalIgnoreCase),
                "StoredPath собственного манифеста обязан находиться в Storage карантина");

            var (restored, restoreMessage) = await service.RestoreItemAsync(item.Id);
            Assert.True(restored, restoreMessage);
            Assert.True(File.Exists(source));
            Assert.Equal("roundtrip content", await File.ReadAllTextAsync(source));

            // Move -> Purge
            Assert.True(await service.MoveToQuarantineAsync(source, "TestCategory"));
            var itemsAgain = await service.GetQuarantinedItemsAsync();
            var itemAgain = Assert.Single(itemsAgain);
            Assert.True(await service.PurgeItemAsync(itemAgain.Id));
            Assert.False(Directory.Exists(Path.GetDirectoryName(itemAgain.StoredPath)));
            Assert.Empty(await service.GetQuarantinedItemsAsync());
        }
        finally
        {
            try { File.Delete(source); } catch { }
        }
    }

    [Fact]
    public async Task PurgeItem_CraftedStoredPathOutsideQuarantine_RefusesAndKeepsVictimDirectory()
    {
        // Purge делает рекурсивный Directory.Delete(GetDirectoryName(StoredPath)):
        // crafted StoredPath указывает на файл жертвы вне карантина.
        var (victimDir, victimFile) = CreateVictim();
        try
        {
            var crafted = MakeItem("crafted0001", victimFile, victimFile);
            WriteCraftedManifest(crafted);
            var service = CreateService();

            var purged = await service.PurgeItemAsync("crafted0001");

            Assert.False(purged, "Purge по подделанному манифесту должен быть отклонён");
            Assert.True(Directory.Exists(victimDir), "Каталог жертвы не должен быть удалён");
            Assert.True(File.Exists(victimFile), "Файл жертвы не должен быть удалён");
        }
        finally
        {
            try { Directory.Delete(victimDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RestoreItem_CraftedOriginalPathOutsideQuarantine_RefusesAndKeepsStoredFile()
    {
        var storedPath = CreateStoredFile("legit0001", "file.txt");
        var victimTarget = Path.Combine(Path.GetTempPath(), $"quarantine_victim_target_{Guid.NewGuid():N}.txt");

        var crafted = MakeItem("crafted0002", storedPath, victimTarget);
        WriteCraftedManifest(crafted);
        var service = CreateService();

        var (success, _) = await service.RestoreItemAsync("crafted0002");

        Assert.False(success, "Restore по подделанному манифесту должен быть отклонён");
        Assert.False(File.Exists(victimTarget), "Файл не должен быть перемещён по crafted OriginalPath");
        Assert.True(File.Exists(storedPath), "Файл должен остаться в хранилище карантина");
    }

    [Fact]
    public async Task RestoreItem_BrokenSignature_Refuses()
    {
        var storedPath = CreateStoredFile("legit0002", "file.txt");
        var originalPath = Path.Combine(Path.GetTempPath(), $"quarantine_restore_target_{Guid.NewGuid():N}.txt");

        var item = MakeItem("item0003", storedPath, originalPath);
        WriteCraftedManifest(item, signature: "AAAAinvalid-signature");
        var service = CreateService();

        var (success, _) = await service.RestoreItemAsync("item0003");

        Assert.False(success, "Битая подпись манифеста должна приводить к отказу");
        Assert.True(File.Exists(storedPath), "Файл должен остаться в хранилище карантина");
        Assert.False(File.Exists(originalPath), "Файл не должен быть восстановлен по битой подписи");
    }

    [Fact]
    public async Task PurgeItem_BrokenSignature_RefusesAndKeepsVictimDirectory()
    {
        var (victimDir, victimFile) = CreateVictim();
        try
        {
            var item = MakeItem("item0004", victimFile, victimFile);
            WriteCraftedManifest(item, signature: "AAAAinvalid-signature");
            var service = CreateService();

            var purged = await service.PurgeItemAsync("item0004");

            Assert.False(purged, "Purge по манифесту с битой подписью должен быть отклонён");
            Assert.True(Directory.Exists(victimDir), "Каталог жертвы не должен быть удалён");
            Assert.True(File.Exists(victimFile), "Файл жертвы не должен быть удалён");
        }
        finally
        {
            try { Directory.Delete(victimDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RestoreItem_LegacyUnsignedManifest_RefusesWithLegacyMessage()
    {
        var storedPath = CreateStoredFile("legacy0001", "file.txt");
        var originalPath = Path.Combine(Path.GetTempPath(), $"quarantine_legacy_target_{Guid.NewGuid():N}.txt");

        var item = MakeItem("legacy0001", storedPath, originalPath);
        // Манифест формата до внедрения подписи: только Items, без Signature.
        WriteCraftedManifest(item);
        var service = CreateService();

        var (success, message) = await service.RestoreItemAsync("legacy0001");

        Assert.False(success, "Манифест устаревшего формата не должен исполняться молча");
        Assert.Contains("устаревшего формата", message);
        Assert.Contains("вручную", message);
        Assert.True(File.Exists(storedPath), "Файл должен остаться в хранилище для восстановления вручную");
    }
}
