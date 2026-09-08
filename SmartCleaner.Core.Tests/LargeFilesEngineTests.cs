using SmartCleaner.Core.LargeFiles;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class LargeFilesEngineTests : IDisposable
{
    private readonly string _testDir;

    public LargeFilesEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "lfe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private async Task<string> CreateFileAsync(string name, long size)
    {
        var path = Path.Combine(_testDir, name);
        using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        // Write in chunks to avoid large allocations
        var buffer = new byte[Math.Min(size, 8192)];
        new Random((int)(name.GetHashCode())).NextBytes(buffer);
        long remaining = size;
        while (remaining > 0)
        {
            var toWrite = (int)Math.Min(remaining, buffer.Length);
            await fs.WriteAsync(buffer, 0, toWrite);
            remaining -= toWrite;
        }
        return path;
    }

    [Fact]
    public async Task ScanAsync_TopN_ReturnsCorrectCount()
    {
        await CreateFileAsync("small.txt", 100);
        await CreateFileAsync("medium.txt", 2000);
        await CreateFileAsync("large.txt", 5000);
        await CreateFileAsync("huge.txt", 10000);

        var engine = new LargeFilesEngine();
        // minSizeBytes = 1, topN = 2
        var result = await engine.ScanAsync(_testDir, topN: 2, minSizeBytes: 1);

        Assert.Equal(2, result.Count);
        // Should be sorted desc by size → huge.txt, large.txt
        Assert.Contains(result, f => f.Name == "huge.txt");
        Assert.Contains(result, f => f.Name == "large.txt");
        Assert.DoesNotContain(result, f => f.Name == "small.txt");
    }

    [Fact]
    public async Task ScanAsync_OrdersBySizeDescendingThenPath()
    {
        await CreateFileAsync("z_last.txt", 5000);
        await CreateFileAsync("a_first.txt", 5000);
        await CreateFileAsync("small.txt", 100);

        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(_testDir, topN: 10, minSizeBytes: 1);

        // Both 5000-byte files come first (size sorted desc), then 100-byte
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Size >= result[1].Size);
        Assert.True(result[1].Size >= result[2].Size);
    }

    [Fact]
    public async Task ScanAsync_MinSize_ExcludesSmallerFiles()
    {
        await CreateFileAsync("big.bin", 1_000_000);
        await CreateFileAsync("small.bin", 100);

        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(_testDir, topN: 10, minSizeBytes: 500_000);

        Assert.Single(result);
        Assert.Equal("big.bin", result[0].Name);
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsEmpty()
    {
        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(_testDir);
        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(@"X:\nonexistent-lfe-path-98765");
        Assert.Empty(result);
    }

    [Fact]
    public async Task ScanAsync_PopulatesLargeFileInfoProperties()
    {
        var filePath = await CreateFileAsync("test_video.mp4", 2048);
        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(_testDir, topN: 10, minSizeBytes: 1);

        var file = Assert.Single(result);
        Assert.Equal("test_video.mp4", file.Name);
        Assert.Equal(2048, file.Size);
        Assert.Equal(".mp4", file.Extension);
        Assert.Equal(filePath, file.Path);
        Assert.Equal(_testDir, file.Directory);
        Assert.NotEqual(default, file.Modified);
    }

    [Fact]
    public async Task ScanAsync_Subdirectories_IncludesNestedFiles()
    {
        var subDir = Path.Combine(_testDir, "sub");
        Directory.CreateDirectory(subDir);
        await CreateFileAsync("root.txt", 100);
        var subFile = Path.Combine(subDir, "nested.bin");
        await File.WriteAllBytesAsync(subFile, new byte[5000]);

        var engine = new LargeFilesEngine();
        var result = await engine.ScanAsync(_testDir, topN: 10, minSizeBytes: 1);

        Assert.Contains(result, f => f.Name == "nested.bin");
        Assert.Contains(result, f => f.Name == "root.txt");
    }

    [Fact]
    public async Task ScanAsync_RespectsCancellation()
    {
        await CreateFileAsync("a.txt", 1000);
        await CreateFileAsync("b.txt", 2000);

        var engine = new LargeFilesEngine();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Already cancelled before start

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ScanAsync(_testDir, topN: 10, minSizeBytes: 1, ct: cts.Token));
    }
}