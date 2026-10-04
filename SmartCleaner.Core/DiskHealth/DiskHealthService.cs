using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using System.Text;
using System.Text.Json;
// UseWindowsForms тянет System.Windows.Forms.ICommandExecutor — снимаем
// неоднозначность в пользу контракта исполнителя команд
using ICommandExecutor = SmartCleaner.Core.Cleaning.ICommandExecutor;

namespace SmartCleaner.Core.DiskHealth;

public class PhysicalDiskInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = "Generic SSD";
    public string MediaType { get; set; } = "SSD";
    public string BusType { get; set; } = "NVMe";
    public string HealthStatus { get; set; } = "Healthy";

    /// <summary>
    /// Остаток ресурса, %. null — телеметрия недоступна, UI показывает «н/д».
    /// Вычисляется из счётчика износа WMI: 100 − Wear (Wear: 0 = новый, 100 = ресурс исчерпан).
    /// </summary>
    public int? RemainingLifePercentage { get; set; }

    /// <summary>Температура, °C. null — телеметрия недоступна, UI показывает «н/д».</summary>
    public int? TemperatureCelsius { get; set; }

    public string RemainingLifeFormatted => RemainingLifePercentage.HasValue ? $"{RemainingLifePercentage.Value}%" : "н/д";
    public string TemperatureFormatted => TemperatureCelsius.HasValue ? $"{TemperatureCelsius.Value} °C" : "н/д";

    public long SizeBytes { get; set; }
    public string SizeFormatted => SizeFormatter.Format(SizeBytes);
    public string TotalBytesWrittenFormatted { get; set; } = "N/A";
    public bool IsSsd => MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) || BusType.Contains("NVMe", StringComparison.OrdinalIgnoreCase);
}

public class DiskHealthService
{
    // Телеметрия S.M.A.R.T. (Get-PhysicalDisk + Get-StorageReliabilityCounter)
    // на реальном железе занимает секунды; 30 с — страховочный потолок.
    // День 24 (reviewer P3): прежде ReadToEnd не был ограничен вовсе —
    // WaitForExit(3000) после чтения не срабатывал никогда
    private static readonly TimeSpan TelemetryTimeout = TimeSpan.FromSeconds(30);

    // Телеметрия S.M.A.R.T. через PowerShell: Get-PhysicalDisk + счётчики надёжности
    // (Wear/Temperature из MSFT_StorageReliabilityCounter). Отсутствующие счётчики
    // остаются null — UI показывает «н/д», никаких выдуманных процентов/градусов.
    // Пролог [Console]::OutputEncoding — UTF-8 в пайп при любой консоли хоста
    // (День 19, срез C): JSON ASCII-safe по именам полей и значениям enum,
    // не-ASCII FriendlyName декодируется корректно
    private const string TelemetryScript =
        "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " +
        "Get-PhysicalDisk | ForEach-Object { " +
        "$c = $null; " +
        "try { $c = $_ | Get-StorageReliabilityCounter -ErrorAction Stop } catch {} " +
        "$w = $null; $t = $null; " +
        "if ($c) { $w = $c.Wear; $t = $c.Temperature } " +
        "[pscustomobject]@{ DeviceId = $_.DeviceId; FriendlyName = $_.FriendlyName; MediaType = $_.MediaType; BusType = $_.BusType; HealthStatus = $_.HealthStatus; Size = $_.Size; Wear = $w; Temperature = $t } " +
        "} | ConvertTo-Json -Compress";

    private readonly ICommandExecutor _commandExecutor;
    private readonly ILogger _logger;

    /// <summary>
    /// День 24 (reviewer P3): телеметрия дисков исполняется через контракт
    /// ICommandExecutor (bounded-таймаут, честный код возврата) с
    /// ILogger-диагностикой вместо Debug.WriteLine. Необязательный
    /// исполнитель — шов для детерминированных тестов (как срезы A-C дней 17-19);
    /// DI-регистрация исполнителя «включает» шов.
    /// </summary>
    public DiskHealthService(ICommandExecutor? commandExecutor = null, ILogger? logger = null)
    {
        _commandExecutor = commandExecutor ?? new ProcessCommandExecutor();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<List<PhysicalDiskInfo>> GetPhysicalDisksHealthAsync()
    {
        var list = new List<PhysicalDiskInfo>();

        try
        {
            var execution = await _commandExecutor.ExecuteAsync(new CommandExecutionRequest
            {
                // Системный Windows PowerShell по абсолютному пути (командлеты
                // Get-PhysicalDisk/Get-StorageReliabilityCounter — системная
                // семантика): неквалифицированное имя = binary planting (M7, День 16б)
                FileName = SystemToolLocator.GetWindowsPowerShellPath(),
                Arguments = ["-NoProfile", "-NonInteractive", "-Command", TelemetryScript],
                WorkingDirectory = string.Empty,
                Timeout = TelemetryTimeout,
                StandardOutputEncoding = Encoding.UTF8
            });

            if (execution.TimedOut || execution.ExitCode != 0)
            {
                // Фейл/таймаут PowerShell — честная причина, а не «пусто» (день 22):
                // телеметрия недоступна, fallback ниже честен
                _logger.LogWarning(
                    "Телеметрия S.M.A.R.T. недоступна: {Reason}",
                    execution.TimedOut ? "таймаут PowerShell" : $"код возврата {execution.ExitCode}");
            }
            else
            {
                ParseJsonOutput(execution.StandardOutput, list);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Телеметрия S.M.A.R.T. дисков недоступна: {Error}", ex.Message);
        }

        // Fallback if empty
        if (list.Count == 0)
        {
            // Только факт существования тома; телеметрия износа/температуры недоступна (null → «н/д»)
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var d in drives)
            {
                list.Add(new PhysicalDiskInfo
                {
                    DeviceId = d.Name,
                    FriendlyName = $"Локальный диск ({d.Name.TrimEnd('\\')})",
                    MediaType = "SSD / HDD",
                    BusType = "SATA/NVMe",
                    HealthStatus = "Healthy",
                    SizeBytes = d.TotalSize
                });
            }
        }

        return list;
    }

    /// <summary>
    /// Разбор JSON-вывода скрипта телеметрии: массив (несколько дисков) или
    /// одиночный объект (один диск). Ничего не выдумывает: не-JSON или пустой
    /// вывод — список остаётся пустым (fallback вызывающего кода).
    /// </summary>
    private static void ParseJsonOutput(string output, List<PhysicalDiskInfo> list)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        if (output.TrimStart().StartsWith("["))
        {
            using var doc = JsonDocument.Parse(output);
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                list.Add(ParseDiskElement(element));
            }
        }
        else if (output.TrimStart().StartsWith("{"))
        {
            using var doc = JsonDocument.Parse(output);
            list.Add(ParseDiskElement(doc.RootElement));
        }
    }

    private static PhysicalDiskInfo ParseDiskElement(JsonElement el)
    {
        var id = el.TryGetProperty("DeviceId", out var pId) ? pId.GetString() ?? "0" : "0";
        var name = el.TryGetProperty("FriendlyName", out var pName) ? pName.GetString() ?? "Physical Disk" : "Physical Disk";
        var media = el.TryGetProperty("MediaType", out var pMedia) ? pMedia.GetString() ?? "SSD" : "SSD";
        var bus = el.TryGetProperty("BusType", out var pBus) ? pBus.GetString() ?? "NVMe" : "NVMe";
        var health = el.TryGetProperty("HealthStatus", out var pHealth) ? pHealth.GetString() ?? "Healthy" : "Healthy";
        var size = el.TryGetProperty("Size", out var pSize) ? pSize.GetInt64() : 0L;

        return new PhysicalDiskInfo
        {
            DeviceId = id,
            FriendlyName = name,
            MediaType = string.IsNullOrWhiteSpace(media) ? "SSD" : media,
            BusType = string.IsNullOrWhiteSpace(bus) ? "NVMe" : bus,
            HealthStatus = health,
            RemainingLifePercentage = ParseRemainingLife(el),
            TemperatureCelsius = ParseTemperature(el),
            SizeBytes = size
        };
    }

    // Wear — счётчик износа WMI в процентах (0 = новый, 100 = ресурс исчерпан;
    // MSFT_StorageReliabilityCounter.Wear). Отсутствует или вне диапазона — null («н/д»).
    private static int? ParseRemainingLife(JsonElement el)
    {
        if (!TryGetInt(el, "Wear", out var wear) || wear < 0 || wear > 100)
            return null;

        return 100 - wear;
    }

    // Temperature — градусы Цельсия (MSFT_StorageReliabilityCounter.Temperature, UInt8).
    // Нулевое значение трактуем как «датчик не отдал данные» (известный сценарий,
    // когда WMI возвращает нули вместо температур) — честнее «н/д», чем «0 °C».
    private static int? ParseTemperature(JsonElement el)
    {
        if (!TryGetInt(el, "Temperature", out var temperature) || temperature <= 0)
            return null;

        return temperature;
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        return el.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt32(out value);
    }
}
