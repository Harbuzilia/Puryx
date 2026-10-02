using System.Security.Cryptography.X509Certificates;

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
    /// <summary>
    /// Резолвит имя исполняемого файла в полный путь.
    /// Известные системные утилиты без пути (MsiExec.exe и т.п.) резолвятся
    /// из системного каталога — не из PATH и не из каталога приложения
    /// (binary planting, см. M7). Остальные имена возвращаются как есть.
    /// </summary>
    internal static string ResolveExecutable(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.IsPathRooted(fileName))
        {
            return fileName;
        }

        var systemCandidate = Path.Combine(Environment.SystemDirectory, fileName);
        return File.Exists(systemCandidate) ? systemCandidate : fileName;
    }

    /// <summary>
    /// Лексическая проверка: файл лежит в Program Files / Program Files (x86) / Windows.
    /// Нормализует путь (GetFullPath), поэтому «..\..» за пределы доверенного корня не проходит.
    /// </summary>
    internal static bool IsTrustedLocation(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathRooted(filePath))
            {
                return false;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            return TrustedRoots().Any(root => !string.IsNullOrEmpty(root) && IsUnder(directory, root));
        }
        catch
        {
            // Недопустимые символы пути и прочие крайности — трактуем как недоверенные
            return false;
        }
    }

    /// <summary>
    /// Факт наличия Authenticode-подписи у файла (без проверки цепочки доверия).
    /// </summary>
    internal static bool HasAuthenticodeSignature(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            _ = X509Certificate.CreateFromSignedFile(filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Политика «можно ли авто-элевировать / тихо запускать»:
    /// доверенная директория ИЛИ наличие Authenticode-подписи.
    /// hasSignature — точка внедрения для тестов (по умолчанию — реальная проверка подписи).
    /// </summary>
    internal static bool IsTrustedExecutable(string fileName, Func<string, bool>? hasSignature = null)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var resolved = ResolveExecutable(fileName);
        if (IsTrustedLocation(resolved))
        {
            return true;
        }

        return (hasSignature ?? HasAuthenticodeSignature)(resolved);
    }

    private static IEnumerable<string> TrustedRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
