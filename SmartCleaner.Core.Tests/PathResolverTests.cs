using System.Diagnostics;
using System.IO;
using SmartCleaner.Core.Safety;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 13 — M1 (ROADMAP, findings M1): все проверки путей в проекте лексические
/// (Path.GetFullPath), junction/reparse-точки не разворачиваются — whitelist-bypass
/// в user-scope (file-items, порождаемые через junction). Целевое поведение
/// PathResolver.ResolveRealPath:
/// 1) путь через junction разворачивается в реальный путь цели;
/// 2) путь без reparse-точек возвращается нормализованным (Path.GetFullPath).
/// Тесты создают реальные junction через cmd mklink /J (права администратора
/// не нужны, в отличие от symlink) и помечены трейтом Integration.
/// </summary>
[Trait("Category", "Integration")]
public class PathResolverTests : IDisposable
{
    private readonly string _root;
    private readonly string _outside;

    public PathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"path_resolver_{Guid.NewGuid():N}");
        _outside = Path.Combine(Path.GetTempPath(), $"path_resolver_target_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        // BCL удаляет junction как ссылку, а не рекурсивно внутрь цели,
        // поэтому порядок удаления каталогов безопасен для содержимого цели.
        try { Directory.Delete(_root, recursive: true); } catch { }
        try { Directory.Delete(_outside, recursive: true); } catch { }
    }

    /// <summary>
    /// Создаёт junction linkPath → targetPath (cmd mklink /J).
    /// Цель может не существовать: «висячие» junction допустимы.
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

    [Fact]
    public void ResolveRealPath_ResolvesJunctionToRealTarget()
    {
        // junction живёт в temp-каталоге _root и указывает наружу — в _outside.
        var realDir = Path.Combine(_outside, "docs");
        Directory.CreateDirectory(realDir);
        var realFile = Path.Combine(realDir, "proof.txt");
        File.WriteAllText(realFile, "inside real target");
        var junction = Path.Combine(_root, "j");
        CreateJunction(junction, _outside);

        // Сканер, перечисляющий через junction, видит путь в терминах ссылки.
        var itemThroughJunction = Path.Combine(junction, "docs", "proof.txt");

        var result = PathResolver.ResolveRealPath(itemThroughJunction);

        Assert.True(result.Resolved, $"junction должен разворачиваться в реальный путь, получено: {result.Path}");
        Assert.Equal(realFile, result.Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRealPath_PlainPath_ReturnsNormalizedSelf()
    {
        var sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);

        // Разделители «/» и «..» — резолв обязан отдать канонический путь.
        var input = $"{_root.Replace('\\', '/')}/sub/../sub/file.txt";

        var result = PathResolver.ResolveRealPath(input);

        Assert.True(result.Resolved);
        Assert.Equal(Path.GetFullPath(Path.Combine(sub, "file.txt")), result.Path, StringComparer.OrdinalIgnoreCase);
    }
}
