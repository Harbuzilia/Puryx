using SmartCleaner.Core.CliInspector;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class CliInspectorTests
{
    [Fact]
    public void PathEnvironmentService_GetEntries_ReturnsValidCollections()
    {
        var service = new PathEnvironmentService();
        var userEntries = service.GetUserPathEntries();
        var sysEntries = service.GetSystemPathEntries();

        Assert.NotNull(userEntries);
        Assert.NotNull(sysEntries);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CliInspectorEngine_ScanAsync_CompletesSuccessfully()
    {
        var pathService = new PathEnvironmentService();
        var engine = new CliInspectorEngine(pathService);

        var result = await engine.ScanAsync();

        Assert.NotNull(result);
        Assert.NotNull(result.Tools);
        Assert.NotNull(result.PathHealth);
        Assert.NotNull(result.PowerShellProfiles);
        Assert.NotNull(result.CategoriesCount);
        Assert.True(result.TotalPathsScanned >= 0);
    }

    [Fact]
    public async Task CliInspectorEngine_ScanAsync_MarksDeadPathAsDeadAndLivePathAsActive()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cli_live_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var deadPath = Path.Combine(Path.GetTempPath(), $"cli_dead_{Guid.NewGuid():N}");
        try
        {
            var engine = new CliInspectorEngine(new FakePathEnvironmentService([tempDir], [deadPath]));

            var result = await engine.ScanAsync();

            var dead = Assert.Single(result.PathHealth, p => p.Path == deadPath);
            Assert.True(dead.IsDead);
            Assert.False(dead.Exists);
            Assert.Equal("System", dead.Scope);
            Assert.Contains("Мертвый путь", dead.Status);

            var live = Assert.Single(result.PathHealth, p => p.Path == tempDir);
            Assert.True(live.Exists);
            Assert.False(live.IsDead);
            Assert.Equal("User", live.Scope);

            Assert.Equal(1, result.DeadPathsCount);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private sealed class FakePathEnvironmentService(
        IReadOnlyList<string> userPaths,
        IReadOnlyList<string> systemPaths) : PathEnvironmentService
    {
        public override List<string> GetUserPathEntries() => [.. userPaths];
        public override List<string> GetSystemPathEntries() => [.. systemPaths];
    }
}
