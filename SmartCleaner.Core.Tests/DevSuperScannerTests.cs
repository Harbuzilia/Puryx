using System.Diagnostics;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using SmartCleaner.Core.Safety;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 14 — M1 2/2 (findings M1): file-items, порождаемые DevSuperScanner сквозь
/// junction, не должны попадать в выдачу (гейт по реальному пути: защищённые
/// цели исключаются, неразрешимая цепочка — fail-closed), а циклический junction
/// под корнями сканера — вешать перечисление (BCL SearchOption.AllDirectories
/// следует за junction до исчерпания длины пути). Junction создаются cmd mklink /J.
/// </summary>
[Trait("Category", "Integration")]
public class DevSuperScannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public DevSuperScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"devsuper_{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"devsuper_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        // Сначала снимаем junction-ссылки (рекурсивный Directory.Delete падает на них).
        foreach (var junction in _junctions)
        {
            try { Directory.Delete(junction); } catch { }
        }

        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_outside, recursive: true); } catch { }
    }

    /// <summary>Сканер с корнем WSL, перенаправленным в тестовое дерево.</summary>
    private sealed class TestableDevSuperScanner : DevSuperScanner
    {
        private readonly string _wslRoot;
        private readonly string _avdRoot;

        public TestableDevSuperScanner(ISafetyService safety, string wslRoot, string avdRoot)
            : base(safety)
        {
            _wslRoot = wslRoot;
            _avdRoot = avdRoot;
        }

        protected override string DirectWslDirectory => _wslRoot;
        protected override string AndroidAvdSnapshotsDirectory => _avdRoot;
    }

    private static ISafetyService CreateRealSafety()
    {
        var config = new InMemoryConfigService([]);
        return new SafetyService(config, new KnowledgeBase(config));
    }

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

    private static ScanResult ScanWithTimeout(IScannerStrategy scanner, TimeSpan limit)
    {
        var scan = Task.Run(() => scanner.ScanAsync().GetAwaiter().GetResult());
        Assert.True(scan.Wait(limit),
            $"скан не завершился за {limit.TotalSeconds:0} с — перечисление повисло (циклический junction?)");
        return scan.Result;
    }

    [Fact]
    public void ScanAsync_CyclicJunctionUnderWslRoot_TerminatesWithoutLoopedItems()
    {
        // Циклический junction (loopA→loopB→loopA) под корнем сканера:
        // скан обязан завершиться за конечное время, легитимный файл — найтись,
        // путей за циклическими ссылками — не быть.
        var wslRoot = Path.Combine(_root, "wsl");
        var realDir = Path.Combine(wslRoot, "real");
        Directory.CreateDirectory(realDir);
        var legitVhdx = Path.Combine(realDir, "ext4.vhdx");
        File.WriteAllText(legitVhdx, "legit-wsl-disk");

        var loopA = Path.Combine(wslRoot, "loopA");
        var loopB = Path.Combine(wslRoot, "loopB");
        CreateJunction(loopA, loopB);
        CreateJunction(loopB, loopA);
        _junctions.Add(loopA);
        _junctions.Add(loopB);

        var scanner = new TestableDevSuperScanner(CreateRealSafety(), wslRoot, Path.Combine(_root, "avd"));

        var result = ScanWithTimeout(scanner, TimeSpan.FromSeconds(30));

        Assert.Contains(result.Items, i => string.Equals(i.Path, legitVhdx, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Items, i => i.Path.Contains("loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanAsync_FileBehindJunctionOutsideWslRoot_NotEmitted()
    {
        // Junction в корне сканера, указывающий наружу: файл за ним не должен
        // попадать в выдачу (перечисление не рекурсирует в reparse-точки).
        var wslRoot = Path.Combine(_root, "wsl2");
        Directory.CreateDirectory(wslRoot);
        var payload = Path.Combine(_outside, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(payload, "ext4.vhdx"), "external-disk");

        var leak = Path.Combine(wslRoot, "leak");
        CreateJunction(leak, payload);
        _junctions.Add(leak);

        var scanner = new TestableDevSuperScanner(CreateRealSafety(), wslRoot, Path.Combine(_root, "avd"));

        var result = ScanWithTimeout(scanner, TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(result.Items, i => i.Path.Contains("leak", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanAsync_WslRootItselfJunctionIntoProtectedTarget_NoItemsFromTarget()
    {
        // Корень сканера — сам junction в защищённую цель (whitelist **\.git\**):
        // файлы цели видны на верхнем уровне без спуска в reparse — их обязан
        // отсекать гейт выдачи по реальному пути.
        var protectedTarget = Path.Combine(_root, "prot", ".git");
        Directory.CreateDirectory(protectedTarget);
        File.WriteAllText(Path.Combine(protectedTarget, "ext4.vhdx"), "protected-disk");

        var wslJunction = Path.Combine(_root, "wslProtected");
        CreateJunction(wslJunction, protectedTarget);
        _junctions.Add(wslJunction);

        var scanner = new TestableDevSuperScanner(CreateRealSafety(), wslJunction, Path.Combine(_root, "avd"));

        var result = ScanWithTimeout(scanner, TimeSpan.FromSeconds(30));

        Assert.False(
            result.Items.Any(i => i.Path.Contains("wslProtected", StringComparison.OrdinalIgnoreCase)),
            "файлы из защищённой цели за junction не должны попадать в выдачу");
    }

    [Fact]
    public void ScanAsync_WslRootItselfJunction_UnprotectedTarget_StillEmitted()
    {
        // Гейт блокирует только защищённые цели, а не любой путь через junction:
        // корень-junction в незащищённую цель продолжает давать items (путь
        // сквозь ссылку, удаление прогонится через резолв в ValidateForDeletion).
        var plainTarget = Path.Combine(_outside, "plain");
        Directory.CreateDirectory(plainTarget);
        var vhdxBehind = Path.Combine(plainTarget, "ext4.vhdx");
        File.WriteAllText(vhdxBehind, "plain-disk");

        var wslJunction = Path.Combine(_root, "wslPlain");
        CreateJunction(wslJunction, plainTarget);
        _junctions.Add(wslJunction);

        var scanner = new TestableDevSuperScanner(CreateRealSafety(), wslJunction, Path.Combine(_root, "avd"));

        var result = ScanWithTimeout(scanner, TimeSpan.FromSeconds(30));

        Assert.Contains(result.Items,
            i => string.Equals(i.Path, Path.Combine(wslJunction, "ext4.vhdx"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanAsync_AndroidSnapshots_CyclicJunctionTerminates_RealSnapshotEmitted()
    {
        // Сайт EnumerateDirectories(..., "snapshots", AllDirectories): циклический
        // junction под корнем AVD не должен рвать перечисление до реальных снимков.
        var avdRoot = Path.Combine(_root, "avd3");
        var snapshots = Path.Combine(avdRoot, "emulator1", "snapshots");
        Directory.CreateDirectory(snapshots);
        File.WriteAllText(Path.Combine(snapshots, "snap.bin"), new string('x', 1024));

        var cycleA = Path.Combine(avdRoot, "loopAlpha");
        var cycleB = Path.Combine(avdRoot, "loopBeta");
        CreateJunction(cycleA, cycleB);
        CreateJunction(cycleB, cycleA);
        _junctions.Add(cycleA);
        _junctions.Add(cycleB);

        var scanner = new TestableDevSuperScanner(
            CreateRealSafety(), Path.Combine(_root, "wsl-missing"), avdRoot);

        var result = ScanWithTimeout(scanner, TimeSpan.FromSeconds(30));

        Assert.Contains(result.Items, i => string.Equals(i.Path, snapshots, StringComparison.OrdinalIgnoreCase));
        // Имена цикл-ссылок не-хексовые: GUID temp-пути их содержать не может.
        Assert.DoesNotContain(result.Items,
            i => i.Path.Contains("loopAlpha", StringComparison.OrdinalIgnoreCase) ||
                 i.Path.Contains("loopBeta", StringComparison.OrdinalIgnoreCase));
    }
}
