using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SmartCleaner.Core.WinSxS;

public class DriverStoreItem
{
    public string PublishedName { get; set; } = string.Empty; // e.g. oem12.inf
    public string OriginalName { get; set; } = string.Empty; // e.g. nv_dispig.inf
    public string ProviderName { get; set; } = string.Empty; // e.g. NVIDIA
    public string ClassName { get; set; } = string.Empty; // e.g. Display
    public string DriverVersion { get; set; } = string.Empty;
    public string DriverDate { get; set; } = string.Empty;
    public bool IsOldDuplicate { get; set; }
    public bool IsSelected { get; set; }
}

public class DriverStoreCleaner
{
    public async Task<List<DriverStoreItem>> ScanDriversAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var drivers = new List<DriverStoreItem>();
        progress?.Report("Опрос установленных драйверов через PnPUtil...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetPnputilPath(),
                Arguments = "/enum-drivers",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return drivers;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            drivers = ParsePnputilOutput(output);
            MarkDuplicateGroups(drivers);
        }
        catch (Exception ex)
        {
            progress?.Report($"Ошибка сканирования драйверов: {ex.Message}");
        }

        return drivers;
    }

    /// <summary>
    /// День 15 — M8, RED-этап: парсинг блоков вывода «pnputil /enum-drivers»,
    /// вынесен из <see cref="ScanDriversAsync"/> без изменения поведения
    /// (compile-enabler для тестов). Известные дефекты текущего поведения:
    /// метка строки версии не покрывает реальные «Driver Version»/«Версия драйвера»
    /// (Win10/11), поле <see cref="DriverStoreItem.DriverDate"/> не заполняется,
    /// а <see cref="DriverStoreItem.DriverVersion"/> хранит всю строку «дата версия».
    /// Реализация — следующим коммитом.
    /// </summary>
    internal static List<DriverStoreItem> ParsePnputilOutput(string output)
    {
        var drivers = new List<DriverStoreItem>();

        // Parse driver blocks
        var blocks = output.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var pubMatch = Regex.Match(block, @"(?:Опубликованное имя|Published Name)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            if (!pubMatch.Success) continue;

            var origMatch = Regex.Match(block, @"(?:Исходное имя|Original Name)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var provMatch = Regex.Match(block, @"(?:Имя поставщика|Provider Name)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var classMatch = Regex.Match(block, @"(?:Имя класса|Class Name)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
            var dateMatch = Regex.Match(block, @"(?:Дата и версия драйвера|Driver date and version)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

            drivers.Add(new DriverStoreItem
            {
                PublishedName = pubMatch.Groups[1].Value.Trim(),
                OriginalName = origMatch.Success ? origMatch.Groups[1].Value.Trim() : "",
                ProviderName = provMatch.Success ? provMatch.Groups[1].Value.Trim() : "",
                ClassName = classMatch.Success ? classMatch.Groups[1].Value.Trim() : "",
                DriverVersion = dateMatch.Success ? dateMatch.Groups[1].Value.Trim() : "",
                IsOldDuplicate = false,
                IsSelected = false
            });
        }

        return drivers;
    }

    /// <summary>
    /// День 15 — M8, RED-этап: разметка дубликатов «как есть» — позиционная:
    /// в группе по OriginalName помечаются все, кроме последнего. Версии/даты
    /// не сравниваются, а порядок pnputil хронологию не гарантирует.
    /// Реальная реализация (группировка по имени INF + классу, сравнение
    /// Version.TryParse с тай-брейком по дате) — следующим коммитом.
    /// </summary>
    internal static void MarkDuplicateGroups(List<DriverStoreItem> drivers)
    {
        // Identify older duplicates by OriginalName or Class+Provider
        var grouped = drivers
            .Where(d => !string.IsNullOrWhiteSpace(d.OriginalName))
            .GroupBy(d => d.OriginalName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var list = group.ToList();
            if (list.Count > 1)
            {
                // Mark all except the latest as old duplicate
                for (int i = 0; i < list.Count - 1; i++)
                {
                    list[i].IsOldDuplicate = true;
                    list[i].IsSelected = true;
                }
            }
        }
    }

    public async Task<(int RemovedCount, List<string> Errors)> RemoveDriversAsync(IEnumerable<DriverStoreItem> drivers, IProgress<string>? progress = null)
    {
        int count = 0;
        var errors = new List<string>();

        foreach (var d in drivers.Where(x => x.IsSelected))
        {
            try
            {
                progress?.Report($"Удаление устаревшего драйвера {d.PublishedName} ({d.ProviderName})...");
                var psi = new ProcessStartInfo
                {
                    FileName = SystemToolLocator.GetPnputilPath(),
                    Arguments = $"/delete-driver {d.PublishedName} /uninstall /force",
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    if (proc.ExitCode == 0) count++;
                    else errors.Add($"Не удалось удалить {d.PublishedName}: код {proc.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Ошибка при удалении {d.PublishedName}: {ex.Message}");
            }
        }

        return (count, errors);
    }
}
