using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class PythonPackagesScannerTests
{
    [Fact]
    public async Task ScanAsync_ReturnsVenvActionableAndGlobalReadonlyPackages()
    {
        var root = CreateTempRoot();
        try
        {
            var project = Path.Combine(root, "py-app");
            var venv = Path.Combine(project, ".venv");
            var scripts = Path.Combine(venv, "Scripts");
            Directory.CreateDirectory(scripts);

            var marker = Path.Combine(venv, "pyvenv.cfg");
            await File.WriteAllTextAsync(marker, "home = C:/Python");

            var pythonExe = Path.Combine(scripts, "python.exe");
            await File.WriteAllTextAsync(pythonExe, "");

            var executor = new RecordingCommandExecutor(request =>
            {
                var isVenv = request.FileName.EndsWith("python.exe", StringComparison.OrdinalIgnoreCase);
                var output = isVenv
                    ? """
                      [
                        { "name": "requests", "version": "2.32.0" }
                      ]
                      """
                    : """
                      [
                        { "name": "pip", "version": "24.0" }
                      ]
                      """;

                return new CommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = output
                };
            });

            var scanner = new PythonPackagesScanner(
                new NoopKnowledgeBase(),
                new AllowAllSafetyService(),
                new InMemoryConfigService([root]),
                executor);

            var result = await scanner.ScanAsync();

            var local = result.Items.Single(i => i.PackageName == "requests");
            Assert.False(local.IsReadonlyInventory);
            Assert.Equal(CleaningActionTarget.Package, local.ActionTarget);
            Assert.Equal(PackageManagerType.Pip, local.PackageManager);
            Assert.Equal(project, local.PackageWorkingDirectory);
            Assert.Equal(pythonExe, local.PackageExecutablePath);

            var global = result.Items.Single(i => i.PackageName == "pip");
            Assert.True(global.IsReadonlyInventory);
            Assert.Equal(RiskCategory.UserData, global.Risk);
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "smartcleaner-py-" + Guid.NewGuid().ToString("N"));
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
