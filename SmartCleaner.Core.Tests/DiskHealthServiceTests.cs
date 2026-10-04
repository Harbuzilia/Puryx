using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.DiskHealth;
using SmartCleaner.Core.Helpers;
using System.Text;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 24 (reviewer P3): телеметрия S.M.A.R.T. дисков исполняется через
/// контракт ICommandExecutor — bounded-таймаут (прежде ReadToEnd не был
/// ограничен вовсе: WaitForExit(3000) после чтения не срабатывал никогда),
/// честный отказ (фейл/таймаут PowerShell ≠ «диски есть») и ILogger вместо
/// Debug.WriteLine. Стаб фиксирует команду: абсолютный путь Windows
/// PowerShell (binary planting, M7), аргументы, таймаут, UTF-8 (день 19).
/// </summary>
public class DiskHealthServiceTests
{
    // Формат ConvertTo-Json реального скрипта телеметрии: массив объектов
    // (несколько дисков). Wear — процент износа (0 = новый), Temperature — °C
    private const string TwoDisksJson = """
        [{"DeviceId":"0","FriendlyName":"Samsung SSD 970","MediaType":"SSD","BusType":"NVMe","HealthStatus":"Healthy","Size":512110190592,"Wear":5,"Temperature":36},
         {"DeviceId":"1","FriendlyName":"WDC Blue HDD","MediaType":"HDD","BusType":"SATA","HealthStatus":"Warning","Size":4000787030016}]
        """;

    private const string SingleDiskJson = """
        {"DeviceId":"0","FriendlyName":"Single NVMe","MediaType":"SSD","BusType":"NVMe","HealthStatus":"Healthy","Size":1024,"Wear":null,"Temperature":null}
        """;

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_IssuesPowerShellThroughExecutor_WithTimeoutAndUtf8()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(TwoDisksJson);
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        // JSON фикстуры разбирается без fallback: два диска с телеметрией
        Assert.Equal(2, disks.Count);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(SystemToolLocator.GetWindowsPowerShellPath(), request.FileName);
        Assert.Equal(["-NoProfile", "-NonInteractive", "-Command"], request.Arguments.Take(3));
        var script = Assert.Single(request.Arguments.Skip(3));
        Assert.Contains("Get-PhysicalDisk", script);
        Assert.Contains("Get-StorageReliabilityCounter", script);
        Assert.Contains("ConvertTo-Json", script);
        // Пролог UTF-8: детерминированное декодирование при любой консоли хоста
        Assert.Contains("[Console]::OutputEncoding", script);
        // Bounded-таймаут вместо неограниченного ReadToEnd (reviewer P3)
        Assert.Equal(TimeSpan.FromSeconds(30), request.Timeout);
        Assert.Equal(Encoding.UTF8, request.StandardOutputEncoding);
    }

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_ParsesWearAndTemperatureHonestly()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(TwoDisksJson);
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        // Wear=5 → остаток 95% (100 − износ); Temperature=36
        Assert.Equal(95, disks[0].RemainingLifePercentage);
        Assert.Equal(36, disks[0].TemperatureCelsius);
        Assert.Equal(512110190592L, disks[0].SizeBytes);
        Assert.True(disks[0].IsSsd);
        // Счётчики отсутствуют (null в JSON) → «н/д», без выдуманных чисел
        Assert.Null(disks[1].RemainingLifePercentage);
        Assert.Null(disks[1].TemperatureCelsius);
    }

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_SingleObjectJson_ParsedWithoutFallback()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(SingleDiskJson);
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        var disk = Assert.Single(disks);
        Assert.Equal("Single NVMe", disk.FriendlyName);
        // Wear/Temperature в JSON отсутствуют как числа → честное «н/д»
        Assert.Null(disk.RemainingLifePercentage);
        Assert.Null(disk.TemperatureCelsius);
    }

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_PowerShellFailure_FallsBackToFixedDrivesWithoutTelemetry()
    {
        // Фейл PowerShell — не «пусто» и не выдуманная телеметрия: fallback
        // по DriveInfo честен — только факт существования тома, износ/температура «н/д»
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "Get-PhysicalDisk : storage service unavailable");
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        AssertFellBackToFixedDrives(disks);
    }

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_TimedOut_FallsBackToFixedDrivesWithoutTelemetry()
    {
        // Bounded-таймаут: телеметрия не уложилась — fallback, процесс не висит
        var executor = new RecordingCommandExecutor();
        executor.EnqueueResult(new CommandExecutionResult { ExitCode = -1, TimedOut = true });
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        AssertFellBackToFixedDrives(disks);
    }

    [Fact]
    public async Task GetPhysicalDisksHealthAsync_MalformedJson_FallsBackToFixedDrivesWithoutTelemetry()
    {
        // Битый JSON — диагностика в лог, пользователь видит fallback, а не крах
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess("{broken json");
        var service = new DiskHealthService(executor);

        var disks = await service.GetPhysicalDisksHealthAsync();

        AssertFellBackToFixedDrives(disks);
    }

    // Fallback по DriveInfo: записи есть (на машине с фиксированными томами),
    // подпись «Локальный диск», телеметрия честно отсутствует
    private static void AssertFellBackToFixedDrives(List<PhysicalDiskInfo> disks)
    {
        Assert.NotEmpty(disks);
        Assert.Contains(disks, d => d.FriendlyName.StartsWith("Локальный диск"));
        Assert.All(disks, d => Assert.Null(d.RemainingLifePercentage));
        Assert.All(disks, d => Assert.Null(d.TemperatureCelsius));
    }
}
