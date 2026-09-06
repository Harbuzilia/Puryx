using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class PackageMaintenanceServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SkipsReadonlyGlobalPackages()
    {
        var projectRoot = CreateTempRoot();
        try
        {
            var executor = new RecordingCommandExecutor();
            var service = new PackageMaintenanceService(new InMemoryConfigService([projectRoot]), executor);

            var result = await service.ExecuteAsync([
                new ScannedItem
                {
                    Path = "global:npm:typescript",
                    Size = 0,
                    LastAccess = DateTime.UtcNow,
                    Risk = RiskCategory.UserData,
                    Description = "readonly",
                    IsDirectory = false,
                    ActionTarget = CleaningActionTarget.Package,
                    PackageManager = PackageManagerType.Npm,
                    PackageName = "typescript",
                    IsReadonlyInventory = true
                }
            ]);

            Assert.Equal(1, result.ProcessedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(0, result.SucceededCount);
            Assert.Empty(executor.Requests);
        }
        finally
        {
            Cleanup(projectRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_RejectsInvalidPackageNameAndUnsafeWorkingDirectory()
    {
        var allowedRoot = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        try
        {
            var executor = new RecordingCommandExecutor();
            var service = new PackageMaintenanceService(new InMemoryConfigService([allowedRoot]), executor);

            var result = await service.ExecuteAsync([
                new ScannedItem
                {
                    Path = "pkg-1",
                    Size = 0,
                    LastAccess = DateTime.UtcNow,
                    Risk = RiskCategory.SafeToDelete,
                    Description = "invalid name",
                    IsDirectory = false,
                    ActionTarget = CleaningActionTarget.Package,
                    PackageManager = PackageManagerType.Npm,
                    PackageName = "left-pad && calc.exe",
                    PackageWorkingDirectory = allowedRoot
                },
                new ScannedItem
                {
                    Path = "pkg-2",
                    Size = 0,
                    LastAccess = DateTime.UtcNow,
                    Risk = RiskCategory.SafeToDelete,
                    Description = "outside root",
                    IsDirectory = false,
                    ActionTarget = CleaningActionTarget.Package,
                    PackageManager = PackageManagerType.Npm,
                    PackageName = "left-pad",
                    PackageWorkingDirectory = outsideRoot
                }
            ]);

            Assert.Equal(2, result.ProcessedCount);
            Assert.Equal(2, result.SkippedCount);
            Assert.Empty(executor.Requests);
            Assert.Contains(result.Errors, e => e.Message.Contains("Недопустимое имя пакета", StringComparison.Ordinal));
            Assert.Contains(result.Errors, e => e.Message.Contains("вне разрешенных границ", StringComparison.Ordinal));
        }
        finally
        {
            Cleanup(allowedRoot);
            Cleanup(outsideRoot);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UsesAllowlistedCommandsWithoutShellInterpolation()
    {
        var projectRoot = CreateTempRoot();
        var pythonExe = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(pythonExe)!);
        await File.WriteAllTextAsync(pythonExe, string.Empty);

        try
        {
            var executor = new RecordingCommandExecutor();
            var service = new PackageMaintenanceService(new InMemoryConfigService([projectRoot]), executor);

            var result = await service.ExecuteAsync([
                new ScannedItem
                {
                    Path = "npm#left-pad",
                    Size = 0,
                    LastAccess = DateTime.UtcNow,
                    Risk = RiskCategory.SafeToDelete,
                    Description = "npm",
                    IsDirectory = false,
                    ActionTarget = CleaningActionTarget.Package,
                    PackageManager = PackageManagerType.Npm,
                    PackageName = "left-pad",
                    PackageWorkingDirectory = projectRoot
                },
                new ScannedItem
                {
                    Path = "pip#requests",
                    Size = 0,
                    LastAccess = DateTime.UtcNow,
                    Risk = RiskCategory.SafeToDelete,
                    Description = "pip",
                    IsDirectory = false,
                    ActionTarget = CleaningActionTarget.Package,
                    PackageManager = PackageManagerType.Pip,
                    PackageName = "requests",
                    PackageWorkingDirectory = projectRoot,
                    PackageExecutablePath = pythonExe
                }
            ]);

            Assert.Equal(2, result.ProcessedCount);
            Assert.Equal(2, result.SucceededCount);
            Assert.Equal(2, executor.Requests.Count);

            var npm = executor.Requests.Single(r => r.FileName == "npm");
            Assert.Equal(projectRoot, npm.WorkingDirectory);
            Assert.Equal(["uninstall", "left-pad", "--no-audit", "--no-fund"], npm.Arguments);

            var pip = executor.Requests.Single(r => r.FileName.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(["-m", "pip", "uninstall", "-y", "requests"], pip.Arguments);
        }
        finally
        {
            Cleanup(projectRoot);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartcleaner-pm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
