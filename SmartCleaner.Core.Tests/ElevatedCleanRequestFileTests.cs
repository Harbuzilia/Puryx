using System.Security.Cryptography;
using System.Text.Json;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class ElevatedCleanRequestFileTests
{
    [Fact]
    public async Task WriteAndReadValidatedAsync_RoundTripsRequest()
    {
        var items = new[]
        {
            new ScannedItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "smartcleaner-test-a"),
                IsDirectory = true,
                Size = 123,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            items,
            ElevatedCleanTargetPolicy.CreateContract(),
            TimeSpan.FromMinutes(10));

        try
        {
            var request = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                launchData.AuthToken,
                TimeSpan.FromMinutes(10));

            Assert.NotNull(request);
            Assert.Single(request!.Items);
            Assert.All(request.Items, item => Assert.True(Path.IsPathFullyQualified(item.Path)));
            Assert.True(ElevatedCleanTargetPolicy.ValidateContract(request.Policy));
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsTamperedPayload()
    {
        var items = new[]
        {
            new ScannedItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "smartcleaner-tamper-test"),
                IsDirectory = true,
                Size = 123,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            items,
            ElevatedCleanTargetPolicy.CreateContract(),
            TimeSpan.FromMinutes(10));

        try
        {
            var rawPayload = await File.ReadAllTextAsync(launchData.RequestFile);
            var request = JsonSerializer.Deserialize<ElevatedCleanRequest>(rawPayload);
            Assert.NotNull(request);

            var tampered = request! with
            {
                Items =
                [
                    new ElevatedCleanRequestItem
                    {
                        Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
                        IsDirectory = true,
                        Size = 1
                    }
                ]
            };

            await File.WriteAllTextAsync(launchData.RequestFile, JsonSerializer.Serialize(tampered));

            var validated = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                launchData.AuthToken,
                TimeSpan.FromMinutes(10));

            Assert.Null(validated);
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }

    [Fact]
    public async Task ReadValidatedAsync_ConsumesNonceOnce()
    {
        var items = new[]
        {
            new ScannedItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "smartcleaner-once-test"),
                IsDirectory = true,
                Size = 1,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            items,
            ElevatedCleanTargetPolicy.CreateContract(),
            TimeSpan.FromMinutes(10));

        try
        {
            var firstRead = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                launchData.AuthToken,
                TimeSpan.FromMinutes(10));

            var secondRead = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                launchData.AuthToken,
                TimeSpan.FromMinutes(10));

            Assert.NotNull(firstRead);
            Assert.Null(secondRead);
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsPathOutsideTemp()
    {
        var nonTempFile = Path.Combine(AppContext.BaseDirectory, "elevated-request.json");
        await File.WriteAllTextAsync(nonTempFile, "{}");

        try
        {
            var request = await ElevatedCleanRequestFile.ReadValidatedAsync(nonTempFile, "unused", TimeSpan.FromMinutes(10));

            Assert.Null(request);
        }
        finally
        {
            if (File.Exists(nonTempFile))
            {
                File.Delete(nonTempFile);
            }
        }
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsInvalidAuthToken()
    {
        // Arrange
        var items = new[]
        {
            new ScannedItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "smartcleaner-token-test"),
                IsDirectory = true,
                Size = 1,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            items,
            ElevatedCleanTargetPolicy.CreateContract(),
            TimeSpan.FromMinutes(10));

        try
        {
            // Act
            var request = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                TimeSpan.FromMinutes(10));

            // Assert
            Assert.Null(request);
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }

    [Fact]
    public async Task ReadValidatedAsync_RejectsRequestOlderThanMaxAge()
    {
        // Arrange
        var items = new[]
        {
            new ScannedItem
            {
                Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "smartcleaner-maxage-test"),
                IsDirectory = true,
                Size = 1,
                Risk = RiskCategory.SafeToDelete,
                Description = "test"
            }
        };

        var launchData = await ElevatedCleanRequestFile.WriteAsync(
            items,
            ElevatedCleanTargetPolicy.CreateContract(),
            TimeSpan.FromMinutes(10));

        try
        {
            await Task.Delay(50);

            // Act
            var request = await ElevatedCleanRequestFile.ReadValidatedAsync(
                launchData.RequestFile,
                launchData.AuthToken,
                TimeSpan.Zero);

            // Assert
            Assert.Null(request);
        }
        finally
        {
            ElevatedCleanRequestFile.TryDeleteResult(launchData.RequestFile);
            ElevatedCleanRequestFile.TryDelete(launchData.RequestFile);
        }
    }
}
