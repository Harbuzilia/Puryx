using Microsoft.Win32;
using SmartCleaner.Core.Localization;
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
            key.SetValue("", Localizer.GetString("Shell_Menu_Analyze"));
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --scan-path \"%1\"");

            return (true, Localizer.GetString("Shell_Msg_AnalyzeRegistered", Localizer.GetString("Shell_Menu_Analyze")));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Register", ex.Message));
        }
    }

    public (bool Success, string Message) UnregisterAnalyzeFolder()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SmartCleanerAnalyze", false);
            return (true, Localizer.GetString("Shell_Msg_Unregistered"));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Unregister", ex.Message));
        }
    }

    public (bool Success, string Message) RegisterShredder()
    {
        try
        {
            var exe = GetAppExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\*\shell\SmartCleanerShred");
            key.SetValue("", Localizer.GetString("Shell_Menu_Shred"));
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --shred-path \"%1\"");

            return (true, Localizer.GetString("Shell_Msg_ShredRegistered", Localizer.GetString("Shell_Menu_Shred")));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Register", ex.Message));
        }
    }

    public (bool Success, string Message) UnregisterShredder()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\*\shell\SmartCleanerShred", false);
            return (true, Localizer.GetString("Shell_Msg_Unregistered"));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Unregister", ex.Message));
        }
    }

    public (bool Success, string Message) RegisterCompact()
    {
        try
        {
            var exe = GetAppExePath();
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\SmartCleanerCompact");
            key.SetValue("", Localizer.GetString("Shell_Menu_Compact"));
            key.SetValue("Icon", $"\"{exe}\",0");

            using var cmdKey = key.CreateSubKey("command");
            cmdKey.SetValue("", $"\"{exe}\" --compact-path \"%1\"");

            return (true, Localizer.GetString("Shell_Msg_CompactRegistered", Localizer.GetString("Shell_Menu_Compact")));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Register", ex.Message));
        }
    }

    public (bool Success, string Message) UnregisterCompact()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\SmartCleanerCompact", false);
            return (true, Localizer.GetString("Shell_Msg_Unregistered"));
        }
        catch (Exception ex)
        {
            return (false, Localizer.GetString("Shell_Err_Unregister", ex.Message));
        }
    }
}
