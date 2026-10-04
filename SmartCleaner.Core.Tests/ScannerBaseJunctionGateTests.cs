using System.Diagnostics;
using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning;
using SmartCleaner.Core.Services;
using SmartCleaner.Core.Safety;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 22 — финализация: гейт выдачи ScannerBase.IsProtectedScanTarget на
/// junction-путях. Гейт резолвит реальный путь (PathResolver.ResolveRealPath)
/// и проверяет whitelist по нему: junction в защищённую цель (встроенный glob
/// «**\.git\**») блокируется, в незащищённую — нет; неразрешимая (циклическая)
/// цепочка — fail-closed. Гейт покрыт напрямую через subclass-обёртку.
/// Junction создаются cmd mklink /J.
/// </summary>
[Trait("Category", "Integration")]
public class ScannerBaseJunctionGateTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public ScannerBaseJunctionGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"gate_{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"gate_out_{Guid.NewGuid():N}");
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

    /// <summary>Обёртка-subclass: открывает protected-гейт для прямого теста.</summary>
    private sealed class TestableScannerBase : ScannerBase
    {
        public TestableScannerBase(ISafetyService safety) : base(safety) { }

        public override string CategoryName => "Test";
        public override string CategoryIcon => "T";
        public override int DisplayOrder => 0;

        public bool ExposeIsProtectedScanTarget(string path) => IsProtectedScanTarget(path);

        public override Task<ScanResult> ScanAsync(
            IProgress<string>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new ScanResult
            {
                CategoryName = CategoryName,
                CategoryIcon = CategoryIcon,
                Items = []
            });
    }

    private static TestableScannerBase CreateScanner()
    {
        var config = new InMemoryConfigService([]);
        return new TestableScannerBase(new SafetyService(config, new KnowledgeBase(config)));
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

    [Fact]
    public void IsProtectedScanTarget_JunctionIntoGitDir_True()
    {
        // Встроенный glob «**\.git\**»: путь через junction лексически «.git» не
        // содержит — гейт обязан резолвнуть реальный путь и заблокировать выдачу
        var gitDir = Path.Combine(_outside, "repo", ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), "git-config");
        var junction = Path.Combine(_root, "link");
        CreateJunction(junction, gitDir);
        _junctions.Add(junction);

        Assert.True(CreateScanner().ExposeIsProtectedScanTarget(Path.Combine(junction, "config")));
    }

    [Fact]
    public void IsProtectedScanTarget_JunctionIntoPlainTarget_False()
    {
        // Гейт блокирует только защищённые цели, а не любой путь через junction:
        // незащищённая цель продолжает выдаваться (удаление прогонится через
        // ValidateForDeletion с тем же резолвом)
        var plain = Path.Combine(_outside, "plain");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "data.tmp"), "plain-data");
        var junction = Path.Combine(_root, "plainlink");
        CreateJunction(junction, plain);
        _junctions.Add(junction);

        Assert.False(CreateScanner().ExposeIsProtectedScanTarget(Path.Combine(junction, "data.tmp")));
    }

    [Fact]
    public void IsProtectedScanTarget_CyclicJunction_TrueFailClosed()
    {
        // Циклический junction (loopA→loopB→loopA): резолв не завершается —
        // гейт fail-closed, путь не выдаётся сканером
        var loopA = Path.Combine(_root, "loopA");
        var loopB = Path.Combine(_root, "loopB");
        CreateJunction(loopA, loopB);
        CreateJunction(loopB, loopA);
        _junctions.Add(loopA);
        _junctions.Add(loopB);

        Assert.True(CreateScanner().ExposeIsProtectedScanTarget(Path.Combine(loopA, "file.tmp")));
    }
}
