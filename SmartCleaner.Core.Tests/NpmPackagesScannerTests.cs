using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class NpmPackagesScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsLocalActionableAndGlobalReadonlyPackages()
    {
        var root = CreateTempRoot();
        try
        {
            var project = Path.Combine(root, "sample-app");
            Directory.CreateDirectory(project);
            var packageJson = Path.Combine(project, "package.json");
            await File.WriteAllTextAsync(packageJson, """
            {
              "dependencies": {
                "left-pad": "1.3.0"
              },
              "devDependencies": {
                "vitest": "1.0.0"
              }
            }
            """);

            var executor = new RecordingCommandExecutor(_ => new CommandExecutionResult
            {
                ExitCode = 0,
                StandardOutput = """
                {
                  "dependencies": {
                    "typescript": { "version": "5.6.0" }
                  }
                }
                """
            });

            var scanner = new NpmPackagesScanner(
                new AllowAllSafetyService(),
                new InMemoryConfigService([root]),
                executor);

            var result = await scanner.ScanAsync();

            var local = result.Items.Where(i => !i.IsReadonlyInventory).ToList();
            var global = result.Items.Where(i => i.IsReadonlyInventory).ToList();

            Assert.Contains(local, i => i.PackageName == "left-pad" && i.PackageWorkingDirectory == project);
            Assert.Contains(local, i => i.PackageName == "vitest" && i.ActionTarget == CleaningActionTarget.Package);

            var globalItem = Assert.Single(global);
            Assert.Equal("typescript", globalItem.PackageName);
            Assert.Equal(RiskCategory.UserData, globalItem.Risk);
            Assert.False(globalItem.IsSelected);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartcleaner-npm-" + Guid.NewGuid().ToString("N"));
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
