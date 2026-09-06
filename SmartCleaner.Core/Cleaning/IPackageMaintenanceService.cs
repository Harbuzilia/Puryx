using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

public interface IPackageMaintenanceService
{
    Task<PackageMaintenanceResult> ExecuteAsync(
        IEnumerable<ScannedItem> items,
        CancellationToken ct = default);

    Task<PackageMaintenanceResult> ExecuteAsync(
        IEnumerable<ScannedItem> items,
        IProgress<CleaningProgress>? progress,
        CancellationToken ct = default);
}
