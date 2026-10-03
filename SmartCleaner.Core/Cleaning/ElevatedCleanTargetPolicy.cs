using SmartCleaner.Core.Safety;

namespace SmartCleaner.Core.Cleaning;

public static class ElevatedCleanTargetPolicy
{
    public const string ContractId = "smartcleaner.elevated-clean.targets.v1";

    public static ElevatedCleanPolicyContract CreateContract()
    {
        return new ElevatedCleanPolicyContract
        {
            ContractId = ContractId,
            AllowedRoots = GetAllowedRoots().ToList()
        };
    }

    public static bool ValidateContract(ElevatedCleanPolicyContract? contract)
    {
        if (contract is null || !string.Equals(contract.ContractId, ContractId, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = GetAllowedRoots().OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var actual = contract.AllowedRoots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (expected.Length != actual.Length)
        {
            return false;
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAllowedTarget(string path)
    {
        // Allowlist-корни проверяются по РЕАЛЬНОМУ пути (findings M1, День 14):
        // junction внутри allowlist-корня, указывающий наружу, лексически проходит
        // проверку границы корня. Неразрешимая reparse-цепочка (цикл, лимит
        // переходов, неподдерживаемый тег — OneDrive-плейсхолдеры, — ошибка
        // доступа) — fail-closed: цель не разрешена.
        var resolved = PathResolver.ResolveRealPath(path);
        if (!resolved.Resolved)
        {
            return false;
        }

        return GetAllowedRoots().Any(root => IsPathWithinRoot(resolved.Path, root));
    }

    private static IEnumerable<string> GetAllowedRoots()
    {
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var systemRoot = Path.GetPathRoot(windowsPath) ?? @"C:\";

        return new[]
        {
            Path.GetFullPath(Path.Combine(windowsPath, "Temp")),
            Path.GetFullPath(Path.Combine(windowsPath, "Prefetch")),
            Path.GetFullPath(Path.Combine(windowsPath, "SoftwareDistribution", "Download")),
            Path.GetFullPath(Path.Combine(windowsPath, "SoftwareDistribution", "DeliveryOptimization")),
            Path.GetFullPath(Path.Combine(windowsPath, "Installer", "$PatchCache$")),
            Path.GetFullPath(Path.Combine(windowsPath, "Minidump")),
            Path.GetFullPath(Path.Combine(windowsPath, "MEMORY.DMP")),
            Path.GetFullPath(Path.Combine(systemRoot, "Windows.old"))
        };
    }

    /// <summary>
    /// Лексическая проверка границы корня. Честна только для пути, уже развёрнутого
    /// из junction/symlink-цепочки (PathResolver.ResolveRealPath, День 14, findings M1):
    /// вызывающий <see cref="IsAllowedTarget"/> резолвит путь до сравнения.
    /// </summary>
    private static bool IsPathWithinRoot(string fullPath, string root)
    {
        var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
