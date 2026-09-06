using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace SmartCleaner.Core.Shell;

public class ExplorerContextMenuManager
{
    private static string GetAppExePath()
    {
        var proc = Process.GetCurrentProcess().MainModule?.FileName;
        return proc ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SmartCleaner.App.exe");
    }

    public bool IsAnalyzeFolderRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\SmartCleanerAnalyze");
        return key != null;
    }

    public bool IsShredderRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\*\shell\SmartCleanerShred");
        return key != null;
    }

    public bool IsCompactRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Directory\shell\SmartCleanerCompact");
        return key != null;
    }

    public (bool Success, string Message) RegisterAnalyzeFolder()
    {
        try
        {
            var exe = GetAppExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\SmartCleanerAnalyze");
            key.SetValue("", "⚡ Анализировать в SmartCleaner");
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --scan-path \"%1\"");

            return (true, "Пункт «⚡ Анализировать в SmartCleaner» успешно добавлен в контекстное меню папок!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка регистрации меню: {ex.Message}");
        }
    }

    public (bool Success, string Message) UnregisterAnalyzeFolder()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SmartCleanerAnalyze", false);
            return (true, "Пункт удален из контекстного меню.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка удаления меню: {ex.Message}");
        }
    }

    public (bool Success, string Message) RegisterShredder()
    {
        try
        {
            var exe = GetAppExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\SmartCleanerShred");
            key.SetValue("", "🔥 Безвозвратно уничтожить (Шредер DoD)");
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --shred-path \"%1\"");

            return (true, "Пункт «🔥 Безвозвратно уничтожить (Шредер DoD)» добавлен в контекстное меню файлов!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка регистрации меню: {ex.Message}");
        }
    }

    public (bool Success, string Message) UnregisterShredder()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SmartCleanerShred", false);
            return (true, "Пункт удален из контекстного меню.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка удаления меню: {ex.Message}");
        }
    }

    public (bool Success, string Message) RegisterCompact()
    {
        try
        {
            var exe = GetAppExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\SmartCleanerCompact");
            key.SetValue("", "🗜️ Сжать через CompactOS (LZX)");
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --compact-path \"%1\"");

            return (true, "Пункт «🗜️ Сжать через CompactOS (LZX)» добавлен в контекстное меню папок!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка регистрации меню: {ex.Message}");
        }
    }

    public (bool Success, string Message) UnregisterCompact()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SmartCleanerCompact", false);
            return (true, "Пункт удален из контекстного меню.");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка удаления меню: {ex.Message}");
        }
    }
}
