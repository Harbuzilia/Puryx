namespace SmartCleaner.Core.Uninstaller;

/// <summary>
/// Политика доверия к командам деинсталляции, взятым из реестра.
/// HKCU-ветку Uninstall может записать любой процесс без прав администратора,
/// поэтому команда из реестра по умолчанию считается недоверенной.
/// Авто-элевация (runas) и тихий запуск разрешены, только если exe:
///  - находится в доверенной директории (Program Files / Windows), или
///  - имеет Authenticode-подпись (факт наличия; полная проверка цепочки
///    WinVerifyTrust в гейт не входит — см. ROADMAP, День 3, H1).
/// </summary>
internal static class UninstallTrustPolicy
{
    // Заглушки для RED-фиксации тестов (коммит 1); реализация — в fix-коммите 2 «не форсировать runas».

    internal static string ResolveExecutable(string fileName)
    {
        return fileName;
    }

    internal static bool IsTrustedLocation(string filePath)
    {
        return false;
    }

    internal static bool HasAuthenticodeSignature(string filePath)
    {
        return false;
    }

    /// <summary>
    /// Политика «можно ли авто-элевировать / тихо запускать»:
    /// доверенная директория ИЛИ наличие Authenticode-подписи.
    /// hasSignature — точка внедрения для тестов (по умолчанию — реальная проверка подписи).
    /// </summary>
    internal static bool IsTrustedExecutable(string fileName, Func<string, bool>? hasSignature = null)
    {
        return false;
    }
}
