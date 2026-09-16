using SmartCleaner.Core.Services;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class ConfigServiceTests
{
    private sealed class ConfigTestData
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    [Fact]
    public void DefaultMode_InTestBinDirectory_IsPortable()
    {
        var service = new ConfigService();

        Assert.Equal(ConfigMode.Portable, service.Mode);
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "config"),
            service.ConfigDirectory, ignoreCase: true);
        Assert.EndsWith("logs", service.LogDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("rules", service.UserRulesDirectory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortableMarkerFile_ForcesPortableMode()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        File.WriteAllText(marker, "portable");
        try
        {
            var service = new ConfigService();

            Assert.Equal(ConfigMode.Portable, service.Mode);
            Assert.Equal(
                Path.Combine(AppContext.BaseDirectory, "config"),
                service.ConfigDirectory, ignoreCase: true);
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void InstalledEnvVariable_SwitchesToAppData()
    {
        var previous = Environment.GetEnvironmentVariable("SMARTCLEANER_MODE");
        Environment.SetEnvironmentVariable("SMARTCLEANER_MODE", "installed");
        try
        {
            var service = new ConfigService();

            Assert.Equal(ConfigMode.Installed, service.Mode);
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SmartCleaner", "config");
            Assert.Equal(expected, service.ConfigDirectory, ignoreCase: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SMARTCLEANER_MODE", previous);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsData()
    {
        var service = new ConfigService();
        var filename = $"test-config-{Guid.NewGuid():N}.json";
        try
        {
            Assert.False(service.Exists(filename));

            service.Save(filename, new ConfigTestData { Name = "alpha", Count = 42 });
            Assert.True(service.Exists(filename));

            var loaded = service.Load<ConfigTestData>(filename);
            Assert.Equal("alpha", loaded.Name);
            Assert.Equal(42, loaded.Count);
        }
        finally
        {
            var path = Path.Combine(service.ConfigDirectory, filename);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaultInstance()
    {
        var service = new ConfigService();

        var loaded = service.Load<ConfigTestData>($"missing-{Guid.NewGuid():N}.json");

        Assert.NotNull(loaded);
        Assert.Equal("", loaded.Name);
        Assert.Equal(0, loaded.Count);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaultInstance()
    {
        var service = new ConfigService();
        var filename = $"corrupt-{Guid.NewGuid():N}.json";
        try
        {
            File.WriteAllText(Path.Combine(service.ConfigDirectory, filename), "{ not valid json !!!");

            var loaded = service.Load<ConfigTestData>(filename);

            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.Count);
        }
        finally
        {
            var path = Path.Combine(service.ConfigDirectory, filename);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
