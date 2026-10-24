using SmartCleaner.Core.DiskHealth;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SmartCleaner.Core.Reporting;

public class SystemReportData
{
    public string ComputerName { get; set; } = Environment.MachineName;
    public string OsVersion { get; set; } = Environment.OSVersion.ToString();
    public string UserName { get; set; } = Environment.UserName;
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
    public string TotalFreedFormatted { get; set; } = "0 B";
    public List<ScannedItem> CleanedItems { get; set; } = [];
    public List<PhysicalDiskInfo> Disks { get; set; } = [];
    public long TotalRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }
}

public class SystemReportGenerator
{
    public async Task<string> GenerateAndOpenReportAsync(SystemReportData data)
    {
        var html = GenerateHtmlReport(data);

        var reportsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartCleaner", "Reports");
        Directory.CreateDirectory(reportsDir);

        var filePath = Path.Combine(reportsDir, $"SmartCleaner_Report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
        await File.WriteAllTextAsync(filePath, html, Encoding.UTF8);

        try
        {
            // Обоснованное исключение (День 24, срез C — shell-запуск): открытие
            // HTML-отчёта в браузере по ассоциации — естественный shell-запуск
            // для пользователя, не исполнение команды: UseShellExecute=true и
            // видимое окно — сам смысл операции, контракт исполнителя
            // (CreateNoWindow, редирект потоков, kill-tree) здесь не применим
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) { Debug.WriteLine($"[SystemReportGenerator] Browser open error: {ex.Message}"); }

        return filePath;
    }

    public string GenerateHtmlReport(SystemReportData data)
    {
        // Язык отчёта следует за выбранным в приложении (null/EN — инвариантная
        // культура): lang-атрибут HTML, формат даты и локализованные строки
        var isRussian = Localizer.Culture?.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase) == true;
        var lang = isRussian ? "ru" : "en";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{lang}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>{Localizer.GetString("Report_Title")}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #0f172a; color: #f8fafc; margin: 0; padding: 40px 20px; }");
        sb.AppendLine(".container { max-width: 900px; margin: 0 auto; background: #1e293b; border-radius: 16px; padding: 32px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); border: 1px solid #334155; }");
        sb.AppendLine(".header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid #334155; padding-bottom: 24px; margin-bottom: 24px; }");
        sb.AppendLine(".title { font-size: 26px; font-weight: 700; color: #38bdf8; }");
        sb.AppendLine(".badge { background: #0284c7; color: white; padding: 6px 14px; border-radius: 20px; font-size: 13px; font-weight: 600; }");
        sb.AppendLine(".grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; margin-bottom: 28px; }");
        sb.AppendLine(".card { background: #0f172a; border-radius: 12px; padding: 18px; border: 1px solid #334155; }");
        sb.AppendLine(".card-title { font-size: 12px; text-transform: uppercase; color: #94a3b8; letter-spacing: 0.05em; margin-bottom: 6px; }");
        sb.AppendLine(".card-value { font-size: 20px; font-weight: 700; color: #f1f5f9; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 16px; font-size: 14px; }");
        sb.AppendLine("th, td { text-align: left; padding: 12px 14px; border-bottom: 1px solid #334155; }");
        sb.AppendLine("th { background: #0f172a; color: #94a3b8; font-weight: 600; }");
        sb.AppendLine(".success { color: #34d399; font-weight: 600; }");
        sb.AppendLine(".btn-print { background: #3b82f6; color: white; border: none; padding: 10px 20px; border-radius: 8px; font-weight: 600; cursor: pointer; float: right; margin-top: 20px; }");
        sb.AppendLine(".btn-print:hover { background: #2563eb; }");
        sb.AppendLine("@media print { .btn-print { display: none; } body { background: white; color: black; } .container { box-shadow: none; border: none; background: white; } .card { background: #f8fafc; border: 1px solid #cbd5e1; } .card-value, .title { color: black; } th { background: #f1f5f9; } }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");

        // Header
        sb.AppendLine("<div class=\"header\">");
        sb.AppendLine("<div>");
        sb.AppendLine("<div class=\"title\">🛡️ Puryx</div>");
        sb.AppendLine($"<div style=\"color: #94a3b8; margin-top: 4px;\">{Localizer.GetString("Report_Subtitle", data.GeneratedAt.ToString(isRussian ? "dd.MM.yyyy HH:mm" : "MMM dd, yyyy HH:mm"))}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"badge\">{Localizer.GetString("Report_Badge")}</div>");
        sb.AppendLine("</div>");

        // System Cards
        sb.AppendLine("<div class=\"grid\">");
        sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{Localizer.GetString("Report_CardComputer")}</div><div class=\"card-value\">{data.ComputerName} ({data.UserName})</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{Localizer.GetString("Report_CardOs")}</div><div class=\"card-value\">{data.OsVersion}</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{Localizer.GetString("Report_CardRam")}</div><div class=\"card-value\">{SizeFormatter.Format(data.TotalRamBytes)}</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{Localizer.GetString("Report_CardFreed")}</div><div class=\"card-value success\">{data.TotalFreedFormatted}</div></div>");
        sb.AppendLine("</div>");

        // Storage / SSD Health Section
        if (data.Disks.Count > 0)
        {
            sb.AppendLine($"<h3 style=\"color: #38bdf8; margin-top: 32px;\">{Localizer.GetString("Report_SmartHeading")}</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<thead><tr><th>{Localizer.GetString("Report_ColDrive")}</th><th>{Localizer.GetString("Report_ColType")}</th><th>{Localizer.GetString("Report_ColBus")}</th><th>{Localizer.GetString("Report_ColSize")}</th><th>{Localizer.GetString("Report_ColHealth")}</th><th>{Localizer.GetString("Report_ColRemaining")}</th><th>{Localizer.GetString("Report_ColTemp")}</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var d in data.Disks)
            {
                sb.AppendLine($"<tr><td><strong>{d.FriendlyName}</strong></td><td>{d.MediaType}</td><td>{d.BusType}</td><td>{d.SizeFormatted}</td><td class=\"success\">{d.HealthStatus}</td><td>{d.RemainingLifeFormatted}</td><td>{d.TemperatureFormatted}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        // Cleaned Items Breakdown
        if (data.CleanedItems.Count > 0)
        {
            sb.AppendLine($"<h3 style=\"color: #38bdf8; margin-top: 32px;\">{Localizer.GetString("Report_CleanedHeading")}</h3>");
            sb.AppendLine("<table>");
            sb.AppendLine($"<thead><tr><th>{Localizer.GetString("Report_ColPath")}</th><th>{Localizer.GetString("Report_ColItemSize")}</th><th>{Localizer.GetString("Report_ColDescription")}</th></tr></thead>");
            sb.AppendLine("<tbody>");
            foreach (var item in data.CleanedItems.Take(25))
            {
                sb.AppendLine($"<tr><td style=\"word-break: break-all;\">{item.Path}</td><td class=\"success\">{SizeFormatter.Format(item.Size)}</td><td>{item.Description}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }

        sb.AppendLine($"<button class=\"btn-print\" onclick=\"window.print()\">{Localizer.GetString("Report_PrintButton")}</button>");
        sb.AppendLine("<div style=\"clear: both;\"></div>");

        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }
}
