using Microsoft.Win32;

namespace SmartCleaner.Core.Privacy;

/// <summary>
/// Снимок значения реестра, прочитанного до изменения.
/// </summary>
/// <param name="Value">Фактическое значение.</param>
/// <param name="Kind">Тип значения в реестре.</param>
public sealed record RegistryValueSnapshot(object Value, RegistryValueKind Kind);

/// <summary>
/// Шов над операциями со значениями реестра, которые нужны
/// PrivacyDebloatService для бэкапа и отката. Абстракция позволяет
/// тестировать бэкап/восстановление детерминированно — фейковое
/// хранилище вместо реестра, без прав и интеграционных трейтов.
/// </summary>
public interface IRegistryValueStore
{
    /// <summary>
    /// Читает значение. null — ключ или значение отсутствуют
    /// (состояние «отсутствовало» для бэкапа).
    /// </summary>
    RegistryValueSnapshot? GetValue(string rootKeyName, string subKeyPath, string valueName);

    /// <summary>
    /// Записывает значение с указанным типом.
    /// RegistryValueKind.Unknown — тип определяет реестр по рантайм-типу
    /// значения (прежнее поведение записи твика).
    /// </summary>
    void SetValue(string rootKeyName, string subKeyPath, string valueName, object value, RegistryValueKind kind);

    /// <summary>
    /// Удаляет значение, если оно существует
    /// (восстановление состояния «отсутствовало»).
    /// </summary>
    void DeleteValue(string rootKeyName, string subKeyPath, string valueName);
}

/// <summary>
/// Реализация поверх реестра HKLM/HKCU (Microsoft.Win32).
/// Корневые ключи не освобождаются: Registry.LocalMachine и
/// Registry.CurrentUser — общие статические ключи.
/// </summary>
public sealed class RegistryValueStore : IRegistryValueStore
{
    private static RegistryKey ResolveRoot(string rootKeyName) =>
        rootKeyName == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

    public RegistryValueSnapshot? GetValue(string rootKeyName, string subKeyPath, string valueName)
    {
        var root = ResolveRoot(rootKeyName);
        using var key = root.OpenSubKey(subKeyPath);
        if (key == null)
        {
            return null;
        }

        var value = key.GetValue(valueName);
        return value == null ? null : new RegistryValueSnapshot(value, key.GetValueKind(valueName));
    }

    public void SetValue(string rootKeyName, string subKeyPath, string valueName, object value, RegistryValueKind kind)
    {
        var root = ResolveRoot(rootKeyName);
        using var key = root.CreateSubKey(subKeyPath, writable: true);
        if (key == null)
        {
            return;
        }

        if (kind == RegistryValueKind.Unknown)
        {
            key.SetValue(valueName, value);
        }
        else
        {
            key.SetValue(valueName, value, kind);
        }
    }

    public void DeleteValue(string rootKeyName, string subKeyPath, string valueName)
    {
        var root = ResolveRoot(rootKeyName);
        using var key = root.OpenSubKey(subKeyPath, writable: true);
        if (key == null)
        {
            return; // ключа нет — значения тоже нет, «отсутствовало» уже восстановлено
        }

        if (key.GetValue(valueName) != null)
        {
            key.DeleteValue(valueName);
        }
    }
}
