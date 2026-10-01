using System.Diagnostics;
using Microsoft.Win32;

namespace SmartCleaner.Core.Shell;

/// <summary>
/// Сервис интеграции с контекстным меню Windows Explorer.
/// Регистрирует/удаляет пункт «Сканировать в Puryx» для папок через реестр.
/// Работает в HKCU (не требует прав администратора).
/// </summary>
public sealed class ContextMenuService
{
    private const string MenuKeyPath = @"Software\Classes\Directory\shell\SmartCleaner";
    private const string CommandKeyPath = @"Software\Classes\Directory\shell\SmartCleaner\command";
    private const string BgMenuKeyPath = @"Software\Classes\Directory\Background\shell\SmartCleaner";
    private const string BgCommandKeyPath = @"Software\Classes\Directory\Background\shell\SmartCleaner\command";

    /// <summary>
    /// Проверяет, зарегистрированы ли записи контекстного меню.
    /// </summary>
    public bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MenuKeyPath);
            return key != null;
        }
        catch (Exception ex) { Debug.WriteLine($"[ContextMenu] Check error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Регистрирует пункт контекстного меню для папок.
    /// Также регистрирует для фона папки (ПКМ по пустому месту).
    /// </summary>
    /// <returns>true если регистрация успешна.</returns>
    public bool Register()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return false;

            var commandValue = $"\"{exePath}\" --scan-path \"%V\"";
            var iconValue = exePath;

            // Для папок (ПКМ по папке)
            using (var key = Registry.CurrentUser.CreateSubKey(MenuKeyPath))
            {
                key.SetValue("", "Сканировать в Puryx");
                key.SetValue("Icon", iconValue);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(CommandKeyPath))
            {
                key.SetValue("", commandValue);
            }

            // Для фона папки (ПКМ по пустому месту)
            using (var key = Registry.CurrentUser.CreateSubKey(BgMenuKeyPath))
            {
                key.SetValue("", "Сканировать текущую папку в Puryx");
                key.SetValue("Icon", iconValue);
            }
            using (var key = Registry.CurrentUser.CreateSubKey(BgCommandKeyPath))
            {
                key.SetValue("", commandValue);
            }

            return true;
        }
        catch (Exception ex) { Debug.WriteLine($"[ContextMenu] Register error: {ex.Message}"); return false; }
    }

    /// <summary>
    /// Удаляет пункт контекстного меню.
    /// </summary>
    /// <returns>true если удаление успешно.</returns>
    public bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(MenuKeyPath, throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(BgMenuKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex) { Debug.WriteLine($"[ContextMenu] Unregister error: {ex.Message}"); return false; }
    }
}
