using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Globalization;
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

    /// <summary>
    /// День 15 — M8: новейшая версия пакета в группе дубликатов («текущий»).
    /// Защищена от предвыбора; помечается при разметке групп, где 2+ записей.
    /// </summary>
    public bool IsCurrent { get; set; }
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
    /// День 15 — M8: парсинг блоков вывода «pnputil /enum-drivers».
    /// Метка строки версии покрывает реальные форматы Win10/11:
    /// «Driver Version»/«Версия драйвера» и «Driver date and version»/
    /// «Дата и версия драйвера»; строка «MM/DD/YYYY x.y.z.w» разделяется
    /// на <see cref="DriverStoreItem.DriverDate"/> и
    /// <see cref="DriverStoreItem.DriverVersion"/>.
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
            // Альтернативы — от длинной метки к короткой: «Версия драйвера» —
            // подстрока «Дата и версия драйвера», порядок предотвращает частичный матч.
            var versionMatch = Regex.Match(block, @"(?:Дата и версия драйвера|Driver date and version|Версия драйвера|Driver Version)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

            var rawLine = versionMatch.Success ? versionMatch.Groups[1].Value.Trim() : "";
            var (datePart, versionPart) = SplitDateAndVersion(rawLine);

            drivers.Add(new DriverStoreItem
            {
                PublishedName = pubMatch.Groups[1].Value.Trim(),
                OriginalName = origMatch.Success ? origMatch.Groups[1].Value.Trim() : "",
                ProviderName = provMatch.Success ? provMatch.Groups[1].Value.Trim() : "",
                ClassName = classMatch.Success ? classMatch.Groups[1].Value.Trim() : "",
                DriverDate = datePart,
                DriverVersion = versionPart,
                IsOldDuplicate = false,
                IsSelected = false
            });
        }

        return drivers;
    }

    /// <summary>
    /// День 15 — M8: разметка дубликатов по идентичности пакета (имя INF + класс)
    /// и сравнению версий (Version.TryParse) с тай-брейком по дате.
    /// Порядок блоков pnputil хронологию не гарантирует, позицией не пользуемся:
    /// строго старшие относительно новейшего — предвыбраны, новейший (и равные ему)
    /// — защищены и помечены «текущий». Не-парсящаяся версия или одиночная запись
    /// в группе — без пометок (fail-safe: нельзя доказать «старее» — не трогаем).
    /// </summary>
    internal static void MarkDuplicateGroups(List<DriverStoreItem> drivers)
    {
        var grouped = drivers
            .Where(d => !string.IsNullOrWhiteSpace(d.OriginalName))
            .GroupBy(d => d.OriginalName.Trim() + "|" + d.ClassName.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            var list = group.ToList();
            if (list.Count < 2) continue; // одиночки не предвыбираются

            var entries = list
                .Select(d => (Item: d, Key: ParseDriverVersionInfo(d.DriverDate, d.DriverVersion)))
                .ToList();

            // Новейший известный ключ группы; записи без версии в ранжировании не участвуют.
            (Version? Version, DateTime? Date)? newest = null;
            foreach (var entry in entries)
            {
                if (entry.Key.Version is null) continue;
                if (newest is null || CompareKeys(entry.Key, newest.Value) > 0)
                    newest = entry.Key;
            }

            if (newest is null) continue; // сравнить нечем — оставляем без пометок (fail-safe)

            foreach (var entry in entries)
            {
                if (entry.Key.Version is null) continue; // версия не распознана — не помечаем

                if (CompareKeys(entry.Key, newest.Value) < 0)
                {
                    // Строго старше новейшего — устаревший дубль, предвыбор.
                    entry.Item.IsOldDuplicate = true;
                    entry.Item.IsSelected = true;
                }
                else
                {
                    // Новейший (в т.ч. равные ему) — «текущий», защищён.
                    entry.Item.IsCurrent = true;
                }
            }
        }
    }

    /// <summary>
    /// День 15 — M8: ключ сравнения драйвера — версия (Version.TryParse)
    /// с тай-брейком по дате (форматы pnputil: MM/dd/yyyy и dd.MM.yyyy).
    /// null-компоненты означают «не распознано»: сравнивать такие записи нельзя.
    /// </summary>
    internal static (Version? Version, DateTime? Date) ParseDriverVersionInfo(string? driverDate, string? driverVersion)
    {
        Version? version = null;
        if (!string.IsNullOrWhiteSpace(driverVersion))
            Version.TryParse(driverVersion.Trim(), out version);

        DateTime? date = null;
        if (TryParseDriverDate(driverDate, out var parsed))
            date = parsed;

        return (version, date);
    }

    private static int CompareKeys((Version? Version, DateTime? Date) x, (Version? Version, DateTime? Date) y)
    {
        var cmp = x.Version!.CompareTo(y.Version);
        if (cmp != 0) return cmp;
        return (x.Date ?? DateTime.MinValue).CompareTo(y.Date ?? DateTime.MinValue);
    }

    private static (string DatePart, string VersionPart) SplitDateAndVersion(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return ("", "");

        // Формат pnputil: «MM/DD/YYYY x.y.z.w» — версия всегда последний токен.
        var tokens = rawLine.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 1 && Version.TryParse(tokens[^1], out _))
            return (string.Join(" ", tokens[..^1]), tokens[^1]);

        if (tokens.Length == 1 && TryParseDriverDate(rawLine, out _))
            return (rawLine, ""); // одиночный токен оказался датой

        return ("", rawLine);
    }

    private static readonly string[] DriverDateFormats =
    {
        "MM/dd/yyyy", "M/d/yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"
    };

    private static bool TryParseDriverDate(string? text, out DateTime date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var format in DriverDateFormats)
        {
            if (DateTime.TryParseExact(text.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }
        return false;
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
