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
        return new DiskUsageScanner(new NoopKnowledgeBase(), new AllowAllSafetyService(), new InMemoryConfigService(roots))
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

    private sealed class InMemoryConfigService(IReadOnlyList<string> paths) : IConfigService
    {
        public ConfigMode Mode => ConfigMode.Portable;
        public string ConfigDirectory => Path.GetTempPath();
        public string LogDirectory => Path.GetTempPath();
        public string UserRulesDirectory => Path.GetTempPath();

        public T Load<T>(string filename) where T : new()
        {
            if (typeof(T) == typeof(ScanPathsConfig))
            {
                return (T)(object)new ScanPathsConfig
                {
                    Paths = paths.ToList()
                };
            }

            return new T();
        }

        public void Save<T>(string filename, T data)
        {
        }

        public bool Exists(string filename) => false;
    }

    private sealed class AllowAllSafetyService : ISafetyService
    {
        public TimeSpan ProtectedPeriod { get; set; }

        public bool IsWhitelisted(string path) => false;
        public void AddToWhitelist(string pattern) { }
        public void RemoveFromWhitelist(string pattern) { }
        public IEnumerable<string> GetWhitelistPatterns() => [];
        public bool IsWithinProtectedPeriod(string path) => false;
        public bool IsFileLocked(string path) => false;
        public void SaveWhitelist() { }
        public void LoadWhitelist() { }

        public DeleteValidation ValidateForDeletion(ScannedItem item)
        {
            return new DeleteValidation
            {
                CanDelete = true,
                RequiresElevation = false
            };
        }
    }

    private sealed class NoopKnowledgeBase : IKnowledgeBase
    {
        public IEnumerable<AppDefinition> GetBuiltInApps() => [];
        public IEnumerable<AppDefinition> GetUserApps() => [];
        public IEnumerable<AppDefinition> GetAllApps() => [];
        public void AddUserApp(AppDefinition app) { }
        public void UpdateUserApp(AppDefinition app) { }
        public void RemoveUserApp(string appId) { }
        public IEnumerable<DiscoveredApp> DiscoverUnknownApps(string rootPath) => [];
        public PathClassification ClassifyPath(string path) => new()
        {
            IsKnownApp = false,
            AppName = null,
            SuggestedRisk = RiskCategory.PerformanceCache,
            Reason = "test"
        };
        public void LoadExternalRules(string jsonPath) { }
        public void SaveUserRules() { }
    }
}
