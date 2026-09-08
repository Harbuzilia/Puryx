using SmartCleaner.Core.Duplicates;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class DuplicateEngineTests
{
    // ─────────────────────────────────────────────
    //  DuplicateGroup — WastedBytes
    // ─────────────────────────────────────────────

    [Fact]
    public void DuplicateGroup_WastedBytes_SingleFile_ReturnsZero()
    {
        var group = new DuplicateGroup
        {
            Key = "1024",
            Files = [new DuplicateFile { Path = @"C:\a.txt", Size = 1024 }]
        };
        Assert.Equal(0, group.WastedBytes);
    }

    [Fact]
    public void DuplicateGroup_WastedBytes_TwoFiles_ReturnsOneFileSize()
    {
        var group = new DuplicateGroup
        {
            Key = "1024",
            Files =
            [
                new DuplicateFile { Path = @"C:\a.txt", Size = 1024 },
                new DuplicateFile { Path = @"C:\b.txt", Size = 1024 }
            ]
        };
        Assert.Equal(1024, group.WastedBytes);
    }

    [Fact]
    public void DuplicateGroup_WastedBytes_FiveFiles_ReturnsFourTimesSize()
    {
        var group = new DuplicateGroup
        {
            Key = "512",
            Files = Enumerable.Range(0, 5).Select(i => new DuplicateFile
            {
                Path = $@"C:\f{i}.txt",
                Size = 512
            }).ToList()
        };
        Assert.Equal(512 * 4, group.WastedBytes);
    }

    [Fact]
    public void DuplicateGroup_WastedBytes_DifferentSizes_UsesFirstFileSize()
    {
        var group = new DuplicateGroup
        {
            Key = "123",
            Files =
            [
                new DuplicateFile { Path = @"C:\big.txt", Size = 1000 },
                new DuplicateFile { Path = @"C:\small.txt", Size = 500 }
            ]
        };
        Assert.Equal(1000, group.WastedBytes);
    }

    // ─────────────────────────────────────────────
    //  DuplicateScanOptions — defaults
    // ─────────────────────────────────────────────

    [Fact]
    public void DuplicateScanOptions_Defaults_BySizeAndByHashEnabled()
    {
        var opts = new DuplicateScanOptions();
        Assert.True(opts.BySize);
        Assert.True(opts.ByHash);
        Assert.False(opts.ByByte);
        Assert.False(opts.ByName);
        Assert.False(opts.TurboMode);
        Assert.Equal(1, opts.MinFileSize);
        Assert.Empty(opts.ExcludePatterns);
    }

    // ─────────────────────────────────────────────
    //  DuplicateFile — Name property
    // ─────────────────────────────────────────────

    [Fact]
    public void DuplicateFile_Name_ExtractsFromPath()
    {
        var file = new DuplicateFile { Path = @"C:\Users\Test\Documents\photo.jpg", Size = 100 };
        Assert.Equal("photo.jpg", file.Name);
    }

    [Fact]
    public void DuplicateFile_Name_RootPath_ReturnsFileName()
    {
        var file = new DuplicateFile { Path = @"C:\file.txt", Size = 100 };
        Assert.Equal("file.txt", file.Name);
    }

    // ─────────────────────────────────────────────
    //  DuplicateEngine — ScanForDuplicatesAsync with temp files
    // ─────────────────────────────────────────────

    [Fact]
    public async Task ScanForDuplicatesAsync_IdenticalFiles_ReturnsOneGroup()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var fileA = Path.Combine(testDir, "a.txt");
            var fileB = Path.Combine(testDir, "b.txt");
            await File.WriteAllTextAsync(fileA, "Hello World Duplicate Test");
            await File.WriteAllTextAsync(fileB, "Hello World Duplicate Test");
            // A third file with different content
            var fileC = Path.Combine(testDir, "c.txt");
            await File.WriteAllTextAsync(fileC, "Different Content Here");

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { ByName = false, ByHash = true, TurboMode = false, MinFileSize = 1 };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);

            // a.txt and b.txt are identical → one group with 2 files
            // c.txt is different → not in a group
            Assert.Single(groups);
            Assert.Equal(2, groups[0].Files.Count);
            Assert.Contains(groups[0].Files, f => f.Path.EndsWith("a.txt"));
            Assert.Contains(groups[0].Files, f => f.Path.EndsWith("b.txt"));
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_TurboHash_DetectsIdenticalFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-turbo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var fileA = Path.Combine(testDir, "alpha.bin");
            var fileB = Path.Combine(testDir, "beta.bin");
            var content = new byte[200_000]; // > 128KB to exercise turbo mode
            new Random(42).NextBytes(content);
            await File.WriteAllBytesAsync(fileA, content);
            await File.WriteAllBytesAsync(fileB, content);

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { ByHash = true, TurboMode = true, MinFileSize = 1 };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);

            Assert.Single(groups);
            Assert.Equal(2, groups[0].Files.Count);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_ByName_GroupingByFileName()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            // Two files with same name in different dirs, same size to pass size-grouping pass
            var subDir = Path.Combine(testDir, "sub");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(testDir, "readme.txt"), "1234567890");
            await File.WriteAllTextAsync(Path.Combine(subDir, "readme.txt"), "ABCDEFGHIJ");

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { BySize = false, ByHash = false, ByName = true, ByByte = false, MinFileSize = 1 };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);

            // Both files named "readme.txt" → should be grouped by name
            Assert.Single(groups);
            Assert.Equal(2, groups[0].Files.Count);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_OnlyBySize_GroupsBySizeOnly()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            // Two files with same size but different content
            var fileA = Path.Combine(testDir, "a.txt");
            var fileB = Path.Combine(testDir, "b.txt");
            await File.WriteAllTextAsync(fileA, "AAAA");
            await File.WriteAllTextAsync(fileB, "BBBB");

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { BySize = true, ByHash = false, ByName = false, MinFileSize = 1 };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);

            // Both are 4 bytes → grouped by size alone
            Assert.Single(groups);
            Assert.Equal(2, groups[0].Files.Count);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_EmptyDirectory_ReturnsEmpty()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions();
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);
            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var engine = new DuplicateEngine();
        var opts = new DuplicateScanOptions();
        var groups = await engine.ScanForDuplicatesAsync([@"X:\nonexistent-path-12345"], opts);
        Assert.Empty(groups);
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_ExcludePatterns_SkipsMatchingFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-excl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            var keepPath = Path.Combine(testDir, "keep.txt");
            var skipPath = Path.Combine(testDir, "skip.tmp");
            await File.WriteAllTextAsync(keepPath, "data12345");
            await File.WriteAllTextAsync(skipPath, "data12345");

            var engine = new DuplicateEngine();
            // *.tmp should exclude skip.tmp; keep.txt remains alone → no groups (need ≥2)
            var opts = new DuplicateScanOptions { ByHash = false, ByName = false, BySize = true, MinFileSize = 1, ExcludePatterns = ["*.tmp"] };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);

            // No files should appear in any group (only keep.txt is non-excluded, needs ≥2 for a group)
            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    [Fact]
    public async Task ScanForDuplicatesAsync_MinFileSize_ExcludesSmallFiles()
    {
        var testDir = Path.Combine(Path.GetTempPath(), "dup-min-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(testDir, "small.txt"), "ab");   // 2 bytes
            await File.WriteAllTextAsync(Path.Combine(testDir, "small2.txt"), "cd");  // 2 bytes

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { BySize = true, ByHash = false, ByName = false, MinFileSize = 100 };
            var groups = await engine.ScanForDuplicatesAsync([testDir], opts);
            Assert.Empty(groups);
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir, true);
        }
    }

    // ─────────────────────────────────────────────
    //  DuplicateEngine — CompareFoldersAsync
    // ─────────────────────────────────────────────

    [Fact]
    public async Task CompareFoldersAsync_IdenticalFolders_ReturnsNoUniqueFiles()
    {
        var dirA = Path.Combine(Path.GetTempPath(), "cmp-a-" + Guid.NewGuid().ToString("N"));
        var dirB = Path.Combine(Path.GetTempPath(), "cmp-b-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dirA, "file.txt"), "same content");
            await File.WriteAllTextAsync(Path.Combine(dirB, "file.txt"), "same content");

            var engine = new DuplicateEngine();
            var result = await engine.CompareFoldersAsync(dirA, dirB);

            Assert.Empty(result.UniqueA);
            Assert.Empty(result.UniqueB);
            Assert.Single(result.Common);
            Assert.Equal("file.txt", result.Common[0].RelativePath);
        }
        finally
        {
            if (Directory.Exists(dirA)) Directory.Delete(dirA, true);
            if (Directory.Exists(dirB)) Directory.Delete(dirB, true);
        }
    }

    [Fact]
    public async Task CompareFoldersAsync_CompletelyDifferent_ReturnsAllUnique()
    {
        var dirA = Path.Combine(Path.GetTempPath(), "cmp-a2-" + Guid.NewGuid().ToString("N"));
        var dirB = Path.Combine(Path.GetTempPath(), "cmp-b2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dirA, "only_in_a.txt"), "content A");
            await File.WriteAllTextAsync(Path.Combine(dirB, "only_in_b.txt"), "content B");

            var engine = new DuplicateEngine();
            var result = await engine.CompareFoldersAsync(dirA, dirB);

            Assert.Single(result.UniqueA);
            Assert.Single(result.UniqueB);
            Assert.Empty(result.Common);
        }
        finally
        {
            if (Directory.Exists(dirA)) Directory.Delete(dirA, true);
            if (Directory.Exists(dirB)) Directory.Delete(dirB, true);
        }
    }

    // ─────────────────────────────────────────────
    //  DuplicateEngine — ScanForSampleAsync
    // ─────────────────────────────────────────────

    [Fact]
    public async Task ScanForSampleAsync_FindsIdenticalCopy()
    {
        var searchDir = Path.Combine(Path.GetTempPath(), "sample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(searchDir);
        try
        {
            var samplePath = Path.Combine(searchDir, "original.txt");
            var copyPath = Path.Combine(searchDir, "copy.txt");
            await File.WriteAllTextAsync(samplePath, "Sample content for testing");
            await File.WriteAllTextAsync(copyPath, "Sample content for testing");

            var engine = new DuplicateEngine();
            var opts = new DuplicateScanOptions { ByHash = true, TurboMode = false, ByByte = false, BySize = true, ByName = false };
            var found = await engine.ScanForSampleAsync(samplePath, [searchDir], opts);

            // Should find the copy, but not the original
            Assert.Single(found);
            Assert.Contains(found, f => f.Path.EndsWith("copy.txt"));
        }
        finally
        {
            if (Directory.Exists(searchDir)) Directory.Delete(searchDir, true);
        }
    }

    [Fact]
    public async Task ScanForSampleAsync_SampleFileNotFound_ReturnsEmpty()
    {
        var engine = new DuplicateEngine();
        var opts = new DuplicateScanOptions();
        var found = await engine.ScanForSampleAsync(@"X:\nonexistent-sample-12345.txt", [@"C:\Windows"], opts);
        Assert.Empty(found);
    }

    // ─────────────────────────────────────────────
    //  DuplicateEngine — ReplaceDuplicatesWithHardlinksAsync
    //  (only tests error handling for cross-volume, not actual hardlink creation)
    // ─────────────────────────────────────────────

    [Fact]
    public async Task ReplaceDuplicatesWithHardlinksAsync_EmptyGroups_ReturnsZero()
    {
        var engine = new DuplicateEngine();
        var (replaced, saved, errors) = await engine.ReplaceDuplicatesWithHardlinksAsync([]);
        Assert.Equal(0, replaced);
        Assert.Equal(0, saved);
        Assert.Empty(errors);
    }
}