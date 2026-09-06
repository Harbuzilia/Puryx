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
        var fullPath = Path.GetFullPath(path);
        return GetAllowedRoots().Any(root => IsPathWithinRoot(fullPath, root));
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
