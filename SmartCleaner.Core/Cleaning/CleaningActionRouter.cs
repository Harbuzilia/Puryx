using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

public static class CleaningActionRouter
{
    public static CleaningActionPartition Partition(IEnumerable<ScannedItem> items)
    {
        var fileSystemItems = new List<ScannedItem>();
        var packageItems = new List<ScannedItem>();

        foreach (var item in items)
        {
            if (item.ActionTarget == CleaningActionTarget.Package)
            {
                packageItems.Add(item);
            }
            else
            {
                fileSystemItems.Add(item);
            }
        }

        return new CleaningActionPartition
        {
            FileSystemItems = fileSystemItems,
            PackageItems = packageItems
        };
    }
}
