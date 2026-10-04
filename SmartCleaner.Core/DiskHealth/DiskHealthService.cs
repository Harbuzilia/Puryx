using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Text.Json;

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
    public async Task<List<PhysicalDiskInfo>> GetPhysicalDisksHealthAsync()
    {
        var list = new List<PhysicalDiskInfo>();

        await Task.Run(() =>
        {
            try
            {
                // Телеметрия S.M.A.R.T. через PowerShell: Get-PhysicalDisk + счётчики надёжности
                // (Wear/Temperature из MSFT_StorageReliabilityCounter). Отсутствующие счётчики
                // остаются null — UI показывает «н/д», никаких выдуманных процентов/градусов.
                var script =
                    "Get-PhysicalDisk | ForEach-Object { " +
                    "$c = $null; " +
                    "try { $c = $_ | Get-StorageReliabilityCounter -ErrorAction Stop } catch {} " +
                    "$w = $null; $t = $null; " +
                    "if ($c) { $w = $c.Wear; $t = $c.Temperature } " +
                    "[pscustomobject]@{ DeviceId = $_.DeviceId; FriendlyName = $_.FriendlyName; MediaType = $_.MediaType; BusType = $_.BusType; HealthStatus = $_.HealthStatus; Size = $_.Size; Wear = $w; Temperature = $t } " +
                    "} | ConvertTo-Json -Compress";
                var psi = new ProcessStartInfo
                {
                    // Системный Windows PowerShell по абсолютному пути (командлеты
                    // Get-PhysicalDisk/Get-StorageReliabilityCounter — системная семантика):
                    // неквалифицированное имя = binary planting (M7, День 16б)
                    FileName = SystemToolLocator.GetWindowsPowerShellPath(),
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    if (!string.IsNullOrWhiteSpace(output))
                    {
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
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[DiskHealthService] WMIC disk info parse error: {ex.Message}"); }

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
        });

        return list;
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
