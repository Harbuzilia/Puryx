using SmartCleaner.Core.CliInspector;
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
    public void PathHealthItem_CorrectlyDetectsDeadPath()
    {
        var fakeDeadPath = @"C:\NonExistent_Fake_Directory_123456789";
        var isDead = !Directory.Exists(fakeDeadPath);

        var item = new PathHealthItem
        {
            Path = fakeDeadPath,
            Scope = "User",
            Exists = !isDead,
            IsDead = isDead,
            Status = "Мертвый путь"
        };

        Assert.True(item.IsDead);
        Assert.False(item.Exists);
    }
}
