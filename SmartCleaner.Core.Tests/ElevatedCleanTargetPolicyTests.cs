using System.Diagnostics;
using System.Security.Principal;
using SmartCleaner.Core.Cleaning;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class ElevatedCleanTargetPolicyTests
{
    [Fact]
    public void ValidateContract_ReturnsTrueForDefaultContract()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract();

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateContract_ReturnsFalseForWrongContractId()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract() with
        {
            ContractId = "smartcleaner.elevated-clean.targets.v0"
        };

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateContract_ReturnsFalseWhenAllowedRootsSetIsModified()
    {
        // Arrange
        var contract = ElevatedCleanTargetPolicy.CreateContract();
        contract.AllowedRoots.RemoveAt(0);

        // Act
        var isValid = ElevatedCleanTargetPolicy.ValidateContract(contract);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsAllowedTarget_ReturnsTrueForPathInsideAllowedRoot()
    {
        // Arrange
        var allowedRoot = ElevatedCleanTargetPolicy.CreateContract().AllowedRoots.First();
        var allowedPath = Path.Combine(allowedRoot, "smartcleaner-policy-test", "file.tmp");

        // Act
        var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(allowedPath);

        // Assert
        Assert.True(allowed);
    }

    [Fact]
    public void IsAllowedTarget_ReturnsFalseForArbitraryPath()
    {
        // Arrange
        var randomPath = Path.Combine(Path.GetTempPath(), "smartcleaner-arbitrary");

        // Act
        var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(randomPath);

        // Assert
        Assert.False(allowed);
    }

    // ─── Junction/reparse (findings M1, День 14 — M1 2/2) ──
    // Allowlist-корни elevated-контура проверяются по РЕАЛЬНОМУ пути цели junction.
    // Тесты создают настоящие junction (cmd mklink /J) — Integration-трейт.

    /// <summary>
    /// Создаёт junction linkPath → targetPath (cmd mklink /J).
    /// </summary>
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe не запущен");
        if (!process.WaitForExit(15_000))
        {
            process.Kill();
            throw new InvalidOperationException($"mklink /J завис: {linkPath} -> {targetPath}");
        }
        Assert.True(Directory.Exists(linkPath),
            $"junction не создан (mklink exit={process.ExitCode}): {linkPath} -> {targetPath}");
    }

    /// <summary>Каталог junction внутри allowlist-корня (C:\Windows\Temp).</summary>
    private static string WindowsTemp =>
        Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));

    /// <summary>
    /// Создание junction в C:\Windows\Temp требует административных прав:
    /// тесты обхода allowlist-корня осмысленно исполняются только в elevated-окружении
    /// (guard, а не skip: в CI без прав они завершаются пусто).
    /// </summary>
    private static bool IsRunningElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Снимает junction-ссылку (нерекурсивное удаление не трогает цель).</summary>
    private static void RemoveJunction(string linkPath)
    {
        try { Directory.Delete(linkPath); } catch { /* best-effort уборка */ }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IsAllowedTarget_JunctionIntoAllowedRoot_ResolvedByRealPath()
    {
        // Junction в пользовательской temp-зоне, указывающий на allowlist-корень
        // C:\Windows\Temp: лексически путь вне корня, реальный путь — внутри.
        // Политика проверяет по реальному пути.
        var root = Path.Combine(Path.GetTempPath(), $"elevated_junction_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var junction = Path.Combine(root, "winTemp");
            CreateJunction(junction, WindowsTemp);

            var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(junction);

            Assert.True(allowed,
                $"путь, резолвящийся в allowlist-корень {WindowsTemp}, должен быть разрешён: {junction}");
        }
        finally
        {
            RemoveJunction(Path.Combine(root, "winTemp"));
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IsAllowedTarget_JunctionInsideAllowedRoot_PointingOutside_Denied()
    {
        // Обход allowlist: junction ВНУТРИ C:\Windows\Temp указывает наружу —
        // лексически путь в корне, реальный путь вне. Обязан быть запрещён.
        if (!IsRunningElevated()) return; // требует прав на запись в C:\Windows\Temp

        var userZone = Path.Combine(Path.GetTempPath(), $"elevated_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(userZone);
        var junction = Path.Combine(WindowsTemp, $"smartcleaner-bypass-{Guid.NewGuid():N}");
        try
        {
            CreateJunction(junction, userZone);
            var itemThroughJunction = Path.Combine(junction, "file.tmp");

            var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(itemThroughJunction);

            Assert.False(allowed,
                $"junction внутри allowlist-корня, указывающий наружу, не должен быть разрешён: {itemThroughJunction}");
        }
        finally
        {
            RemoveJunction(junction);
            try { Directory.Delete(userZone, recursive: true); } catch { }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void IsAllowedTarget_SelfReferencingJunctionInsideAllowedRoot_DeniedFailClosed()
    {
        // Неразрешимая (циклическая) reparse-цепочка внутри allowlist-корня:
        // лексически путь в корне, но резолв невозможен — fail-closed, не разрешён.
        if (!IsRunningElevated()) return; // требует прав на запись в C:\Windows\Temp

        var junction = Path.Combine(WindowsTemp, $"smartcleaner-self-{Guid.NewGuid():N}");
        try
        {
            CreateJunction(junction, junction); // самоссылка
            var itemThroughJunction = Path.Combine(junction, "file.tmp");

            var allowed = ElevatedCleanTargetPolicy.IsAllowedTarget(itemThroughJunction);

            Assert.False(allowed,
                $"неразрешимая reparse-цепочка внутри корня не должна быть разрешена: {itemThroughJunction}");
        }
        finally
        {
            RemoveJunction(junction);
        }
    }
}
