using Microsoft.Win32;
using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.IO;

namespace SmartCleaner.Core.Uninstaller;

public class UninstallerEngine
{
    public async Task<List<InstalledAppItem>> ScanInstalledAppsAsync(CancellationToken ct = default)
    {
        var apps = new List<InstalledAppItem>();

        await Task.Run(() =>
        {
            // 1. 64-bit Local Machine
            ScanRegistryUninstallKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", apps);

            // 2. 32-bit Local Machine (WOW6432Node)
            ScanRegistryUninstallKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", apps);

            // 3. Current User
            ScanRegistryUninstallKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall", apps);
        }, ct);

        // Deduplicate and filter out empty / system components
        var unique = apps
            .GroupBy(a => a.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Where(a => !string.IsNullOrWhiteSpace(a.DisplayName) && !a.IsSystemComponent && !string.IsNullOrWhiteSpace(a.UninstallString))
            .OrderBy(a => a.DisplayName)
            .ToList();

        return unique;
    }

    public async Task<(bool Success, string Message)> UninstallAppAsync(InstalledAppItem app, bool silent = false)
    {
        if (app == null || string.IsNullOrWhiteSpace(app.UninstallString))
            return (false, "Команда деинсталляции отсутствует");

        var cmd = silent && !string.IsNullOrWhiteSpace(app.QuietUninstallString)
            ? app.QuietUninstallString
            : app.UninstallString;

        try
        {
            var (fileName, args) = ParseCommand(cmd);

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas" // elevated
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return (proc.ExitCode == 0, $"Деинсталлятор завершил работу с кодом {proc.ExitCode}");
            }
            return (false, "Не удалось запустить процесс деинсталлятора");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка запуска деинсталлятора: {ex.Message}");
        }
    }

    private void ScanRegistryUninstallKey(RegistryKey root, string subPath, List<InstalledAppItem> list)
    {
        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    using var appKey = key.OpenSubKey(subKeyName);
                    if (appKey == null) continue;

                    var name = appKey.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var publisher = appKey.GetValue("Publisher")?.ToString() ?? "";
                    var version = appKey.GetValue("DisplayVersion")?.ToString() ?? "";
                    var installDate = appKey.GetValue("InstallDate")?.ToString() ?? "";
                    var installLoc = appKey.GetValue("InstallLocation")?.ToString() ?? "";
                    var uninstallStr = appKey.GetValue("UninstallString")?.ToString() ?? "";
                    var quietUninstall = appKey.GetValue("QuietUninstallString")?.ToString() ?? "";
                    var isSys = (int?)(appKey.GetValue("SystemComponent") as int?) == 1;

                    long size = 0;
                    if (appKey.GetValue("EstimatedSize") is int sz)
                    {
                        size = (long)sz * 1024; // Registry stores EstimatedSize in KB
                    }
                    else if (!string.IsNullOrWhiteSpace(installLoc) && Directory.Exists(installLoc))
                    {
                        try
                        {
                            size = Directory.EnumerateFiles(installLoc, "*", SearchOption.AllDirectories)
                                .Sum(f => { try { return new FileInfo(f).Length; } catch (Exception ex) { Debug.WriteLine($"[UninstallerEngine] File size error: {ex.Message}"); return 0; } });
                        }
                        catch (Exception ex) { Debug.WriteLine($"[UninstallerEngine] Install location size error: {ex.Message}"); }
                    }

                    list.Add(new InstalledAppItem
                    {
                        Id = $"{root.Name}_{subKeyName}",
                        DisplayName = name,
                        Publisher = publisher,
                        DisplayVersion = version,
                        InstallDate = installDate,
                        InstallLocation = installLoc,
                        UninstallString = uninstallStr,
                        QuietUninstallString = quietUninstall,
                        EstimatedSizeBytes = size,
                        EstimatedSizeFormatted = SizeFormatter.Format(size),
                        RegistryKeyPath = $@"{root.Name}\{subPath}\{subKeyName}",
                        IsSystemComponent = isSys,
                        AppType = uninstallStr.Contains("MsiExec", StringComparison.OrdinalIgnoreCase) ? "MSI" : "Win32"
                    });
                }
                catch (Exception ex) { Debug.WriteLine($"[UninstallerEngine] Registry subkey error: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[UninstallerEngine] Registry uninstall key scan error: {ex.Message}"); }
    }

    internal static (string FileName, string Arguments) ParseCommand(string commandLine)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.StartsWith("\""))
        {
            var closingQuote = trimmed.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                var file = trimmed.Substring(1, closingQuote - 1);
                var args = trimmed.Substring(closingQuote + 1).Trim();
                return (file, args);
            }
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            return (trimmed.Substring(0, spaceIdx), trimmed.Substring(spaceIdx + 1));
        }

        return (trimmed, string.Empty);
    }
}
