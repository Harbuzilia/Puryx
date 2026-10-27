using SmartCleaner.Core.Models;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты persistence опций трея/авто-проверки (ROADMAP v3.1, День 13, фаза A):
/// дефолты приватности (обе опции выключены) и roundtrip через config-каталог —
/// тот же механизм Load/Save, что у Language/ScanSettings. Состояние последней
/// авто-проверки переживает перезапуск с точностью до UTC-момента.
/// </summary>
public class TraySettingsTests : IDisposable
{
    private readonly string _configDirectory =
        Path.Combine(Path.GetTempPath(), $"puryx-tray-{Guid.NewGuid():N}");

    private readonly FileBackedConfigService _config;

    public TraySettingsTests()
    {
        _config = new FileBackedConfigService(_configDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_configDirectory))
            {
                Directory.Delete(_configDirectory, recursive: true);
            }
        }
        catch
        {
            // Уборка тестового каталога не влияет на результат теста
        }
    }

    [Fact]
    public void Defaults_ArePrivacySafe_BothOptionsOff()
    {
        var settings = _config.Load<TraySettingsConfig>(TraySettingsConfig.Filename);

        Assert.False(settings.MinimizeToTray);
        Assert.False(settings.AutoCheckUpdates);
    }

    [Fact]
    public void Options_RoundTrip_ThroughConfigDirectory()
    {
        _config.Save(TraySettingsConfig.Filename, new TraySettingsConfig
        {
            MinimizeToTray = true,
            AutoCheckUpdates = true
        });

        var loaded = _config.Load<TraySettingsConfig>(TraySettingsConfig.Filename);

        Assert.True(loaded.MinimizeToTray);
        Assert.True(loaded.AutoCheckUpdates);
    }

    [Fact]
    public void LastAutoCheckUtc_RoundTrips_AsInstant()
    {
        var lastCheck = new DateTimeOffset(2026, 10, 27, 10, 15, 30, TimeSpan.FromHours(3));

        _config.Save(UpdateStateConfig.Filename, new UpdateStateConfig { LastAutoCheckUtc = lastCheck });
        var loaded = _config.Load<UpdateStateConfig>(UpdateStateConfig.Filename);

        Assert.NotNull(loaded.LastAutoCheckUtc);
        Assert.Equal(lastCheck.ToUniversalTime(), loaded.LastAutoCheckUtc!.Value.ToUniversalTime());
    }
}