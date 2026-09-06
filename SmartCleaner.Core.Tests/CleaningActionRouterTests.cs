using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class CleaningActionRouterTests
{
    [Fact]
    public void Partition_SplitsFilesystemAndPackageActions()
    {
        var items = new[]
        {
            new ScannedItem
            {
                Path = @"C:\temp\a.log",
                Size = 1,
                LastAccess = DateTime.UtcNow,
                Risk = RiskCategory.SafeToDelete,
                Description = "file",
                IsDirectory = false,
                ActionTarget = CleaningActionTarget.FileSystem
            },
            new ScannedItem
            {
                Path = "project/package.json#left-pad",
                Size = 0,
                LastAccess = DateTime.UtcNow,
                Risk = RiskCategory.SafeToDelete,
                Description = "pkg",
                IsDirectory = false,
                ActionTarget = CleaningActionTarget.Package,
                PackageManager = PackageManagerType.Npm,
                PackageName = "left-pad",
                PackageWorkingDirectory = @"C:\projects\demo"
            }
        };

        var partition = CleaningActionRouter.Partition(items);

        Assert.Single(partition.FileSystemItems);
        Assert.Single(partition.PackageItems);
        Assert.Equal(@"C:\temp\a.log", partition.FileSystemItems[0].Path);
        Assert.Equal("left-pad", partition.PackageItems[0].PackageName);
    }
}
