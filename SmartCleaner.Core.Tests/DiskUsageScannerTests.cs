using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class DiskUsageScannerTests
{
    [Fact]
    public async Task ScanAsync_OrdersDeterministicallyBySizeThenPath()
    {
        var root = CreateTempRoot();
        try
        {
            var alpha = Path.Combine(root, "alpha.log");
            var bravo = Path.Combine(root, "bravo.log");
            var charlie = Path.Combine(root, "charlie.log");

            await WriteSizedFileAsync(alpha, 200);
            await WriteSizedFileAsync(bravo, 300);
            await WriteSizedFileAsync(charlie, 300);

            var scanner = CreateScanner([root]);

            var result = await scanner.ScanAsync();

            Assert.True(result.Items.Count >= 3);
            Assert.Equal(bravo, result.Items[0].Path);
            Assert.Equal(charlie, result.Items[1].Path);
            Assert.Equal(alpha, result.Items[2].Path);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ScanAsync_SkipsInvalidRootsAndContinuesWithAccessibleOnes()
    {
        var root = CreateTempRoot();
        try
        {
            var dataFile = Path.Combine(root, "data.log");
            await WriteSizedFileAsync(dataFile, 128);

            var missing = Path.Combine(root, "missing-root");
            var scanner = CreateScanner([missing, dataFile, root]);

            var result = await scanner.ScanAsync();

            Assert.Contains(result.Items, item => item.Path == dataFile);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ScanAsync_RespectsCancellation()
    {
        var root = CreateTempRoot();
        try
        {
            var deep = Path.Combine(root, "deep");
            Directory.CreateDirectory(deep);
            for (var i = 0; i < 200; i++)
            {
                await WriteSizedFileAsync(Path.Combine(deep, $"file-{i}.tmp"), 8 * 1024);
            }

            var scanner = CreateScanner([root]);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(ct: cts.Token));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task ScanAsync_SkipsReparsePointDirectories()
    {
        var root = CreateTempRoot();
        try
        {
            var target = Path.Combine(root, "target-data");
            Directory.CreateDirectory(target);
            var heavy = Path.Combine(target, "heavy.log");
            await WriteSizedFileAsync(heavy, 1024);

            var link = Path.Combine(root, "link-data");
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
            {
                return;
            }

            var scanner = CreateScanner([root]);
            var result = await scanner.ScanAsync();

            Assert.Contains(result.Items, item => item.Path == heavy);
            Assert.DoesNotContain(result.Items, item => item.Path.StartsWith(link, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static DiskUsageScanner CreateScanner(IReadOnlyList<string> roots)
    {
        return new DiskUsageScanner(new AllowAllSafetyService(), new InMemoryConfigService(roots))
        {
            MinInsightBytes = 1,
            MaxDepth = 10,
            MaxItems = 100
        };
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartcleaner-disk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task WriteSizedFileAsync(string path, int size)
    {
        var bytes = new byte[size];
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
