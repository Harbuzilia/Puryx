using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using Xunit;

namespace SmartCleaner.Core.Tests;

internal sealed class DirectProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}

public class CleaningServiceProgressTests
{
    [Fact]
    public async Task CleanAsync_ReportsProcessedCountAfterEachItem_AndEndsAt100Percent()
    {
        // Arrange
        var testRoot = Path.Combine(Path.GetTempPath(), "smartcleaner-progress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);

        var firstFile = Path.Combine(testRoot, "one.tmp");
        var secondFile = Path.Combine(testRoot, "two.tmp");
        await File.WriteAllTextAsync(firstFile, "1");
        await File.WriteAllTextAsync(secondFile, "2");

        var items = new[]
        {
            new ScannedItem
            {
                Path = firstFile,
                IsDirectory = false,
                Size = 1,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            },
            new ScannedItem
            {
                Path = secondFile,
                IsDirectory = false,
                Size = 1,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var service = new CleaningService(new AllowAllSafetyService())
        {
            Mode = CleaningMode.Permanent
        };

        var updates = new List<CleaningProgress>();
        var progress = new DirectProgress<CleaningProgress>(updates.Add);

        try
        {
            // Act
            var result = await service.CleanAsync(items, progress);

            // Assert
            Assert.Equal(2, result.DeletedCount);
            Assert.True(updates.Count >= 2);
            Assert.Contains(updates, p => p.ProcessedCount == 1 && p.TotalCount == 2);

            var final = updates.Last();
            Assert.Equal(2, final.ProcessedCount);
            Assert.Equal(2, final.TotalCount);
            Assert.Equal(100d, final.Percentage);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, true);
            }
        }
    }
}
