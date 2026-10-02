using Microsoft.Win32;
using SmartCleaner.Core.Helpers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SmartCleaner.Core.Uninstaller;

public class UninstallerEngine
{
    private const int ErrorElevationRequired = 740; // ERROR_ELEVATION_REQUIRED
    private const int ErrorCancelled = 1223;        // ERROR_CANCELLED

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

        var (fileName, args) = ParseCommand(cmd);
        var exePath = UninstallTrustPolicy.ResolveExecutable(fileName);

        // H1: тихий (batch) запуск команды из реестра — только для доверенных exe
        // (Program Files/Windows или Authenticode-подпись). HKCU-ветку пишет любой
        // процесс: молчаливый запуск недоверенного кода недопустим.
        if (silent && !UninstallTrustPolicy.IsTrustedExecutable(exePath))
        {
            return (false,
                $"Тихая деинсталляция отклонена: «{fileName}» не находится в доверенной зоне " +
                "(Program Files / Windows) и не имеет Authenticode-подписи. " +
                "Запустите деинсталляцию в интерактивном режиме.");
        }

        try
        {
            // H1: runas не форсируем. Команда взята из реестра (HKCU-ветку Uninstall
            // может записать любой процесс без прав), поэтому принудительное повышение
            // недоверенной команды недопустимо. Честный деинсталлятор запросит права
            // сам через манифест — увидим ERROR_ELEVATION_REQUIRED и решим по политике доверия.
            var proc = Process.Start(BuildStartInfo(exePath, args));
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                return (proc.ExitCode == 0, $"Деинсталлятор завершил работу с кодом {proc.ExitCode}");
            }
            return (false, "Не удалось запустить процесс деинсталлятора");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            // Деинсталлятору нужны права администратора (его манифест).
            // Повторный запуск с runas — только после политики доверия:
            // доверенная директория (Program Files/Windows) или Authenticode-подпись.
            if (!UninstallTrustPolicy.IsTrustedExecutable(exePath))
            {
                return (false,
                    $"Деинсталлятор требует повышения прав, но «{fileName}» не признан доверенным " +
                    "(не находится в Program Files/Windows и не имеет Authenticode-подписи). " +
                    "Автоматическое повышение прав команды из реестра отклонено из соображений безопасности.");
            }

            try
            {
                var proc = Process.Start(BuildStartInfo(exePath, args, elevate: true));
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    return (proc.ExitCode == 0, $"Деинсталлятор завершил работу с кодом {proc.ExitCode}");
                }
                return (false, "Не удалось запустить процесс деинсталлятора с повышением прав");
            }
            catch (Win32Exception ex2) when (ex2.NativeErrorCode == ErrorCancelled)
            {
                return (false, "Повышение прав отменено пользователем");
            }
            catch (Exception ex2)
            {
                return (false, $"Ошибка запуска деинсталлятора с повышением прав: {ex2.Message}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка запуска деинсталлятора: {ex.Message}");
        }
    }

    private static ProcessStartInfo BuildStartInfo(string fileName, string args, bool elevate = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = true
        };

        if (elevate)
            psi.Verb = "runas"; // только осознанный повтор после политики доверия

        return psi;
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

            // Незакрытая кавычка (мусор в реестре вида «"C:\App\Un.exe /S»):
            // убираем ведущую кавычку и парсим остаток как команду без кавычек
            trimmed = trimmed.TrimStart('"').Trim();
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx > 0)
        {
            return (trimmed.Substring(0, spaceIdx), trimmed.Substring(spaceIdx + 1));
        }

        return (trimmed, string.Empty);
    }
}
