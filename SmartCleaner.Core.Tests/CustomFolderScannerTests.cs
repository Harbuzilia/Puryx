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
/// День 22 — финализация: reparse-гвард в CustomFolderScanner.ScanPattern.
/// Стек рекурсии прежде толкал подкаталоги без проверки ReparsePoint: junction
/// внутрь корня сканирования разворачивался в бесконечный спуск (корень →
/// junction → корень …), находя целевую папку заново на каждом уровне —
/// фантомные копии; junction наружу отдавал чужое содержимое как находки
/// выбранной папки. Фикс — по образцу ScannerBase.SafeEnumerateDirectoriesRecursive
/// (День 14): спуск в reparse-точки запрещён. Junction создаются cmd mklink /J.
/// </summary>
[Trait("Category", "Integration")]
public class CustomFolderScannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;
    private readonly List<string> _junctions = [];

    public CustomFolderScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"customscan_{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"customscan_out_{Guid.NewGuid():N}");
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

    private static CustomFolderScanner CreateScanner(string rootPath)
    {
        var config = new ScanConfiguration { CustomScanPath = rootPath };
        var safetyConfig = new InMemoryConfigService([]);
        return new CustomFolderScanner(config, new SafetyService(safetyConfig, new KnowledgeBase(safetyConfig)));
    }

    /// <summary>node_modules с файлом ≥ 1 МБ — проходит порог размера ScanPattern.</summary>
    private static string CreateNodeModules(string parent, string fileName)
    {
        var dir = Path.Combine(parent, "node_modules");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), new string('x', 1_200_000));
        return dir;
    }

    [Fact]
    public void ScanAsync_CyclicJunctionInsideRoot_TerminatesWithoutPhantomDuplicates()
    {
        // Junction «loop» указывает на сам корень: без reparse-гварда стек
        // рекурсии уходит в бесконечный спуск (root\loop, root\loop\loop, …),
        // а node_modules находится заново на каждом уровне вложенности —
        // фантомные копии одной и той же физической папки.
        var realNodeModules = CreateNodeModules(_root, "package.bin");
        var loop = Path.Combine(_root, "loop");
        CreateJunction(loop, _root);
        _junctions.Add(loop);

        var result = ScanWithTimeout(CreateScanner(_root), TimeSpan.FromSeconds(30));

        // Легитимная находка — ровно одна; путей за циклической ссылкой нет
        // (имя «loop» не-хексовое: GUID temp-пути его содержать не может)
        Assert.Single(result.Items,
            i => string.Equals(i.Path, realNodeModules, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Items,
            i => i.Path.Contains("loop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanAsync_JunctionOutsideRoot_DoesNotLeakExternalNodeModules()
    {
        // Junction в корне сканирования, указывающий наружу: node_modules за
        // ним — чужое содержимое, а не мусор выбранной пользователем папки.
        // Спуск в reparse-точку обязан быть закрыт.
        CreateNodeModules(_outside, "payload.bin");
        var leak = Path.Combine(_root, "leak");
        CreateJunction(leak, _outside);
        _junctions.Add(leak);

        var result = ScanWithTimeout(CreateScanner(_root), TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(result.Items,
            i => i.Path.Contains("leak", StringComparison.OrdinalIgnoreCase));
    }
}
