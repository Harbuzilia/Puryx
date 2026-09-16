using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace SmartCleaner.Core.CliInspector;

public class PathEnvironmentService
{
    private const int HWND_BROADCAST = 0xffff;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    public virtual List<string> GetUserPathEntries()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User);
            if (string.IsNullOrWhiteSpace(raw)) return [];
            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                      .Select(p => p.Trim())
                      .Where(p => !string.IsNullOrEmpty(p))
                      .ToList();
        }
        catch
        {
            return [];
        }
    }

    public virtual List<string> GetSystemPathEntries()
    {
        try
        {
            var raw = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(raw)) return [];
            return raw.Split(';', StringSplitOptions.RemoveEmptyEntries)
                      .Select(p => p.Trim())
                      .Where(p => !string.IsNullOrEmpty(p))
                      .ToList();
        }
        catch
        {
            return [];
        }
    }

    public (bool Success, string? Error, int RemovedCount) CleanDeadUserPaths()
    {
        try
        {
            var entries = GetUserPathEntries();
            var validEntries = entries.Where(Directory.Exists).ToList();
            var deadCount = entries.Count - validEntries.Count;

            if (deadCount == 0)
            {
                return (true, null, 0);
            }

            var newPathValue = string.Join(";", validEntries);
            Environment.SetEnvironmentVariable("PATH", newPathValue, EnvironmentVariableTarget.User);
            BroadcastEnvironmentChange();

            return (true, null, deadCount);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 0);
        }
    }

    public (bool Success, string? Error) RemoveUserPathEntry(string pathToRemove)
    {
        try
        {
            var entries = GetUserPathEntries();
            var updated = entries.Where(p => !string.Equals(p, pathToRemove, StringComparison.OrdinalIgnoreCase)).ToList();

            if (updated.Count == entries.Count)
            {
                return (true, null); // Nothing changed
            }

            var newPathValue = string.Join(";", updated);
            Environment.SetEnvironmentVariable("PATH", newPathValue, EnvironmentVariableTarget.User);
            BroadcastEnvironmentChange();

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeout(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                2000,
                out _);
        }
        catch
        {
            // Ignore broadcast failure if any
        }
    }
}
