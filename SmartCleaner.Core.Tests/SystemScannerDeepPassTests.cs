using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Deep-pass системного мусора (ROADMAP v3.1, День 15, фаза F): <c>.chk</c>/<c>.tmp</c>/<c>.dmp</c>
/// ищутся рекурсивно ТОЛЬКО по явному opt-in (<see cref="ScanSettingsConfig.DeepJunkScan"/>),
/// ТОЛЬКО внутри ограниченных корней (в тестах — шов <c>deepScanRoots</c>), ТОЛЬКО файлы
/// больше нуля байт. Whitelist и защищённый период применяются как в обычном проходе:
/// сканер не выдаёт то, что потом запрещено удалять.
///
/// Фикстура — изолированные каталоги под %TEMP% теста: «пользовательский temp» глубже
/// на два уровня (рекурсия обязательна), «Windows\Temp», пустой файл, посторонние
/// расширения и файл ВНЕ корней (границы прохода).
/// </summary>
public class SystemScannerDeepPassTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"puryx-deep-{Guid.NewGuid():N}");
    private readonly string _userTempRoot;
    private readonly string _winTempRoot;
    private readonly string _outsideRoot;

    public SystemScannerDeepPassTests()
    {
        _userTempRoot = Path.Combine(_root, "UserTemp");
        _winTempRoot = Path.Combine(_root, "WindowsTemp");
        _outsideRoot = Path.Combine(_root, "Outside");

        // Корень 1 (аналог %TEMP%): плоский .chk, вложенный .tmp (2 уровня), .dmp,
        // пустой .tmp, посторонние расширения (.txt, .tmp.bak).
        WriteFile(Path.Combine(_userTempRoot, "chkdsk0001.chk"), 1024);
        WriteFile(Path.Combine(_userTempRoot, "nested", "session.tmp"), 512);
        WriteFile(Path.Combine(_userTempRoot, "nested", "deep", "app.dmp"), 2048);
        WriteFile(Path.Combine(_userTempRoot, "empty.tmp"), 0);
        WriteFile(Path.Combine(_userTempRoot, "notes.txt"), 4096);
        WriteFile(Path.Combine(_userTempRoot, "backup.tmp.bak"), 300);

        // Корень 2 (аналог C:\Windows\Temp)
        WriteFile(Path.Combine(_winTempRoot, "installer.tmp"), 256);

        // Вне корней: не должен попасть в выдачу ни при каких условиях
        WriteFile(Path.Combine(_outsideRoot, "outside.tmp"), 4096);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Уборка тестового каталога не влияет на результат
        }
    }

    private static void WriteFile(string path, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    /// <summary>Элементы deep-pass: только пути внутри фикстурных корней.</summary>
    private List<ScannedItem> DeepItems(IEnumerable<ScannedItem> items) =>
        items.Where(i => IsUnder(i.Path, _userTempRoot) || IsUnder(i.Path, _winTempRoot)).ToList();

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private SystemScanner CreateScanner(bool deepJunkScan, SmartCleaner.Core.Safety.ISafetyService? safety = null)
    {
        var config = new InMemoryConfigService([])
        {
            ScanSettings = new ScanSettingsConfig { DeepJunkScan = deepJunkScan }
        };
        return new SystemScanner(
            safety ?? new AllowAllSafetyService(),
            config,
            [_userTempRoot, _winTempRoot]);
    }

    /// <summary>Опция выключена (дефолт) — deep-pass не выполняется вообще.</summary>
    [Fact]
    public async Task ScanAsync_DeepJunkOff_ReturnsNoDeepItems()
    {
        var scanner = CreateScanner(deepJunkScan: false);

        var result = await scanner.ScanAsync();

        Assert.Empty(DeepItems(result.Items));
    }

    /// <summary>
    /// Свежий конфиг (без файла настроек) — опция выключена: расширенный проход
    /// не включается «сам» и не зависит от того, что кто-то забыл записать файл.
    /// </summary>
    [Fact]
    public async Task ScanAsync_DefaultConfig_KeepsDeepPassDisabled()
    {
        var config = new InMemoryConfigService([]); // ScanSettings == null → new ScanSettingsConfig()
        var scanner = new SystemScanner(new AllowAllSafetyService(), config, [_userTempRoot, _winTempRoot]);

        var result = await scanner.ScanAsync();

        Assert.Empty(DeepItems(result.Items));
    }

    /// <summary>
    /// Опция включена: .chk/.tmp/.dmp из ВСЕХ ограниченных корней (включая вложенные
    /// каталоги) — SafeToDelete, файлы (не каталоги), с честными текстами.
    /// </summary>
    [Fact]
    public async Task ScanAsync_DeepJunkOn_FindsChkTmpDmp_AsSafeToDelete()
    {
        var scanner = CreateScanner(deepJunkScan: true);

        var result = await scanner.ScanAsync();

        var deep = DeepItems(result.Items);
        Assert.Equal(4, deep.Count);
        Assert.All(deep, item =>
        {
            Assert.Equal(RiskCategory.SafeToDelete, item.Risk);
            Assert.True(item.IsSelected);
            Assert.False(item.IsDirectory);
            Assert.True(item.Size > 0);
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.False(string.IsNullOrWhiteSpace(item.Explanation));
            Assert.False(string.IsNullOrWhiteSpace(item.HowToRestore));
        });

        Assert.Contains(deep, i => i.Path.EndsWith("chkdsk0001.chk", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deep, i => i.Path.EndsWith("session.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deep, i => i.Path.EndsWith("app.dmp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(deep, i => i.Path.EndsWith("installer.tmp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Фильтры: пустой файл и посторонние расширения не выдаются, файл вне
    /// ограниченных корней не попадает в выдачу (границы прохода соблюдены).
    /// </summary>
    [Fact]
    public async Task ScanAsync_DeepJunkOn_AppliesExtensionSizeAndRootBounds()
    {
        var scanner = CreateScanner(deepJunkScan: true);

        var result = await scanner.ScanAsync();

        Assert.DoesNotContain(result.Items, i => i.Path.EndsWith("empty.tmp", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Items, i => i.Path.EndsWith("notes.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Items, i => i.Path.EndsWith("backup.tmp.bak", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Items, i => i.Path.EndsWith("outside.tmp", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whitelist — вето и для deep-pass: защищённый путь не выдаётся сканером
    /// (не «выдаётся, но не удаляется»).
    /// </summary>
    [Fact]
    public async Task ScanAsync_DeepJunkOn_WhitelistBlocksItems()
    {
        var scanner = CreateScanner(deepJunkScan: true, safety: new BlockingSafetyService());

        var result = await scanner.ScanAsync();

        Assert.Empty(DeepItems(result.Items));
    }

    /// <summary>
    /// Персист опции в общем scan_settings.json: дефолт выключен, соседние
    /// настройки (MFT-режим, cookie-opt-in) не теряются при read-modify-write.
    /// </summary>
    [Fact]
    public void DeepJunkScan_DefaultsOff_AndRoundTripsThroughConfigDirectory()
    {
        Assert.False(new ScanSettingsConfig().DeepJunkScan);

        var configDirectory = Path.Combine(_root, "config");
        var config = new FileBackedConfigService(configDirectory);

        config.Save(ScanSettingsConfig.Filename, new ScanSettingsConfig
        {
            MftScanMode = "On",
            BrowserCleanCookies = true,
            DeepJunkScan = true
        });

        var loaded = config.Load<ScanSettingsConfig>(ScanSettingsConfig.Filename);

        Assert.True(loaded.DeepJunkScan);
        Assert.True(loaded.BrowserCleanCookies);
        Assert.Equal("On", loaded.MftScanMode);
    }
}