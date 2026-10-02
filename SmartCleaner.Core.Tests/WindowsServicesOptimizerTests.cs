using SmartCleaner.Core.ServicesOpt;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 4 — H2 (ROADMAP): ложный успех при настройке служб.
/// sc.exe для несуществующей службы завершается кодом 1060 — SetServiceStartupAsync
/// обязан возвращать false по фактическому ExitCode, а не «true после WaitForExit».
/// Полные контрактные тесты ExitCode со стабами исполнителя — День 22 (P3).
/// </summary>
public class WindowsServicesOptimizerTests
{
    [Fact]
    public async Task SetServiceStartupAsync_NonExistentService_ReturnsFalse()
    {
        var optimizer = new WindowsServicesOptimizer();

        // sc config на несуществующей службе печатает «FAILED 1060:
        // The specified service does not exist» и возвращает код 1060 —
        // успех невозможен, вызов не меняет систему
        var result = await optimizer.SetServiceStartupAsync(
            "PuryxUnitTestNoSuchService", ServiceStartupType.Disabled);

        Assert.False(result);
    }
}
