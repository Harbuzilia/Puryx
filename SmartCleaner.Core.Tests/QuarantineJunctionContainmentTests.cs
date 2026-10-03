using SmartCleaner.Core.Safety;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 16б: containment StoredPath в карантине обязан учитывать reparse-точки.
/// Лексическое сравнение (Path.GetFullPath + префикс) пропускает junction внутри
/// Storage, указывающий наружу: Storage\j → каталог жертвы. Тесты создают реальные
/// junction (cmd mklink /J, прав администратора не нужно) и манифест, подписанный
/// валидным ключом, — проверяется именно слой containment: валидная подпись сама
/// по себе не защита, когда crafted-путь легально «внутри» Storage лексически.
/// Трейт Integration: нужны реальные junction в temp-каталоге.
/// </summary>
[Trait("Category", "Integration")]
public class QuarantineJunctionContainmentTests : IDisposable
{
    private readonly string _root;
    private readonly string _storageDir;
    private readonly string _manifestFile;
    private readonly string _configDirectory;
    private readonly FileBackedConfigService _config;

    /// <summary>Известный тесту per-install ключ: сервис загрузит его из ConfigDirectory.</summary>
    private readonly byte[] _key;

    public QuarantineJunctionContainmentTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"quarantine_junction_{Guid.NewGuid():N}");
        _storageDir = Path.Combine(_root, "Storage");
        _manifestFile = Path.Combine(_root, "manifest.json");
        Directory.CreateDirectory(_storageDir);

        _configDirectory = Path.Combine(Path.GetTempPath(), $"quarantine_junction_config_{Guid.NewGuid():N}");
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
        // BCL удаляет junction как ссылку, а не рекурсивно внутрь цели, поэтому
        // порядок удаления безопасен для содержимого каталогов жертв.
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_configDirectory, recursive: true); } catch { }
    }

    private QuarantineService CreateService() => new(_root, _config);

    /// <summary>
    /// Создаёт junction linkPath → targetPath (cmd mklink /J).
    /// </summary>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe не запущен");
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new InvalidOperationException($"mklink /J завис: {linkPath} -> {targetPath}");
        }
        Assert.True(Directory.Exists(linkPath),
            $"junction не создан (mklink exit={process.ExitCode}): {linkPath} -> {targetPath}");
    }

    /// <summary>Манифест с ВАЛИДНОЙ подписью известного тесту ключа: атака подписью не отбивается.</summary>
    private void WriteSignedManifest(QuarantinedItem item)
    {
        var manifest = new QuarantineManifest { Items = new List<QuarantinedItem> { item } };
        manifest.Signature = QuarantineManifestSigner.ComputeSignature(manifest, _key);
        File.WriteAllText(_manifestFile, JsonSerializer.Serialize(manifest));
    }

    private static QuarantinedItem MakeItem(string id, string storedPath, string originalPath, bool isDirectory) => new()
    {
        Id = id,
        OriginalPath = originalPath,
        StoredPath = storedPath,
        Name = Path.GetFileName(originalPath),
        SizeBytes = 1,
        QuarantinedAt = new DateTime(2026, 1, 2, 3, 4, 5),
        IsDirectory = isDirectory
    };

    [Fact]
    public async Task RestoreItem_StoredPathEscapesStorageThroughJunction_Refuses()
    {
        var victimDir = Path.Combine(Path.GetTempPath(), $"quarantine_junction_victim_{Guid.NewGuid():N}");
        var victimFile = Path.Combine(victimDir, "important.txt");
        Directory.CreateDirectory(victimDir);
        File.WriteAllText(victimFile, "victim data");
        var restoreTarget = Path.Combine(Path.GetTempPath(), $"quarantine_junction_moved_{Guid.NewGuid():N}.txt");

        var junction = Path.Combine(_storageDir, "j");
        CreateJunction(junction, victimDir);
        try
        {
            // StoredPath лексически строго внутри Storage, но реально — файл жертвы снаружи.
            var storedPath = Path.Combine(junction, "important.txt");
            WriteSignedManifest(MakeItem("junction0001", storedPath, restoreTarget, isDirectory: false));
            var service = CreateService();

            var (success, message) = await service.RestoreItemAsync("junction0001");

            Assert.False(success,
                "Restore обязан отклонять StoredPath, уходящий из Storage через junction, даже при валидной подписи");
            Assert.Contains("вне каталога карантина", message);
            Assert.True(File.Exists(victimFile), "Файл жертвы не должен быть перемещён из каталога вне карантина");
            Assert.False(File.Exists(restoreTarget), "Файл не должен появиться по crafted OriginalPath");
        }
        finally
        {
            try { Directory.Delete(victimDir, recursive: true); } catch { }
            try { File.Delete(restoreTarget); } catch { }
        }
    }

    [Fact]
    public async Task PurgeItem_StoredPathEscapesStorageThroughJunction_Refuses()
    {
        var victimDir = Path.Combine(Path.GetTempPath(), $"quarantine_junction_victim_{Guid.NewGuid():N}");
        var victimSub = Path.Combine(victimDir, "sub");
        var victimFile = Path.Combine(victimSub, "important.txt");
        Directory.CreateDirectory(victimSub);
        File.WriteAllText(victimFile, "victim data");

        var junction = Path.Combine(_storageDir, "j");
        CreateJunction(junction, victimDir);
        try
        {
            // Purge рекурсивно удаляет GetDirectoryName(StoredPath): лексически это
            // Storage\j\sub, реально — подкаталог жертвы вне карантина.
            var storedPath = Path.Combine(junction, "sub", "important.txt");
            WriteSignedManifest(MakeItem("junction0002", storedPath, victimFile, isDirectory: true));
            var service = CreateService();

            var (success, message) = await service.PurgeItemAsync("junction0002");

            Assert.False(success,
                "Purge обязан отклонять StoredPath, уходящий из Storage через junction, даже при валидной подписи");
            Assert.Contains("вне каталога карантина", message);
            Assert.True(File.Exists(victimFile), "Файлы жертвы не должны быть рекурсивно удалены через junction");
        }
        finally
        {
            try { Directory.Delete(victimDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RestoreItem_UnresolvableReparseChainInStoredPath_FailsClosed()
    {
        // Циклические junction (a→b, b→a): цепочка reparse-точек не разрешается,
        // containment обязан отказать (fail-closed), а не проверять путь лексически.
        var a = Path.Combine(_storageDir, "a");
        var b = Path.Combine(_storageDir, "b");
        CreateJunction(a, b);
        CreateJunction(b, a);

        var storedPath = Path.Combine(a, "file.txt");
        var restoreTarget = Path.Combine(Path.GetTempPath(), $"quarantine_junction_moved_{Guid.NewGuid():N}.txt");
        WriteSignedManifest(MakeItem("junction0003", storedPath, restoreTarget, isDirectory: false));
        var service = CreateService();

        var (success, message) = await service.RestoreItemAsync("junction0003");

        Assert.False(success, "Нерезолвуемая цепочка reparse-точек в StoredPath — fail-closed отказ");
        Assert.Contains("вне каталога карантина", message);
    }
}
