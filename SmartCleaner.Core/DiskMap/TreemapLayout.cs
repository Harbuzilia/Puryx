using SmartCleaner.Core.Helpers;
using System.IO;

namespace SmartCleaner.Core.DiskMap;

public class TreemapRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public DiskNode Node { get; set; } = new();
    public string Color { get; set; } = "#3B82F6";
    public string FormattedSize => SizeFormatter.Format(Node.Size);
    public string DisplayName => Node.Name;
    public string FullPath => Node.FullPath;
    public bool IsFolder => !Node.IsFile;
}

public static class TreemapLayout
{
    public static List<TreemapRect> CalculateSquarified(DiskNode rootNode, double boundsWidth, double boundsHeight)
    {
        var result = new List<TreemapRect>();
        if (rootNode == null || rootNode.Children.Count == 0 || boundsWidth <= 0 || boundsHeight <= 0)
            return result;

        var totalSize = rootNode.Children.Sum(c => c.Size);
        if (totalSize <= 0) return result;

        var items = rootNode.Children
            .Where(c => c.Size > 0)
            .OrderByDescending(c => c.Size)
            .ToList();

        LayoutRow(items, 0, 0, boundsWidth, boundsHeight, totalSize, result);
        return result;
    }

    private static void LayoutRow(
        List<DiskNode> items,
        double x, double y,
        double width, double height,
        long totalAreaSize,
        List<TreemapRect> result)
    {
        if (items.Count == 0 || width <= 1 || height <= 1) return;

        if (items.Count == 1)
        {
            var single = items[0];
            result.Add(CreateRect(single, x, y, width, height));
            return;
        }

        var isHorizontal = width >= height;
        long remainingTotal = items.Sum(i => i.Size);

        // Simple slice and dice / aspect ratio optimizer
        var halfSize = remainingTotal / 2;
        var splitIndex = 0;
        long accumulated = 0;

        for (int i = 0; i < items.Count; i++)
        {
            accumulated += items[i].Size;
            if (accumulated >= halfSize || i == items.Count - 1)
            {
                splitIndex = i + 1;
                break;
            }
        }

        var firstHalf = items.Take(splitIndex).ToList();
        var secondHalf = items.Skip(splitIndex).ToList();

        var firstSum = firstHalf.Sum(i => i.Size);
        var ratio = (double)firstSum / remainingTotal;

        if (isHorizontal)
        {
            var firstWidth = width * ratio;
            LayoutBlock(firstHalf, x, y, firstWidth, height, firstSum, result);
            LayoutBlock(secondHalf, x + firstWidth, y, width - firstWidth, height, remainingTotal - firstSum, result);
        }
        else
        {
            var firstHeight = height * ratio;
            LayoutBlock(firstHalf, x, y, width, firstHeight, firstSum, result);
            LayoutBlock(secondHalf, x, y + firstHeight, width, height - firstHeight, remainingTotal - firstSum, result);
        }
    }

    private static void LayoutBlock(
        List<DiskNode> items,
        double x, double y,
        double width, double height,
        long totalSize,
        List<TreemapRect> result)
    {
        if (items.Count == 0 || width <= 1 || height <= 1) return;

        if (items.Count == 1)
        {
            result.Add(CreateRect(items[0], x, y, width, height));
            return;
        }

        var isHorizontal = width >= height;
        double currentPos = isHorizontal ? x : y;

        foreach (var item in items)
        {
            var fraction = (double)item.Size / totalSize;
            if (isHorizontal)
            {
                var itemWidth = width * fraction;
                result.Add(CreateRect(item, currentPos, y, itemWidth, height));
                currentPos += itemWidth;
            }
            else
            {
                var itemHeight = height * fraction;
                result.Add(CreateRect(item, x, currentPos, width, itemHeight));
                currentPos += itemHeight;
            }
        }
    }

    private static TreemapRect CreateRect(DiskNode node, double x, double y, double w, double h)
    {
        return new TreemapRect
        {
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = Math.Max(1, w - 2), // 2px margin for tile grid
            Height = Math.Max(1, h - 2),
            Node = node,
            Color = GetColorForNode(node)
        };
    }

    private static string GetColorForNode(DiskNode node)
    {
        if (!node.IsFile)
        {
            // Folder colors by depth
            return (node.Depth % 4) switch
            {
                0 => "#2563EB", // Blue
                1 => "#7C3AED", // Purple
                2 => "#0D9488", // Teal
                _ => "#4F46E5"  // Indigo
            };
        }

        var ext = Path.GetExtension(node.FullPath).ToLowerInvariant();
        return ext switch
        {
            // Videos
            ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" => "#8B5CF6", // Purple
            // Archives & ISO
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".iso" or ".vhd" or ".vhdx" => "#F59E0B", // Amber
            // Executables & Installers
            ".exe" or ".msi" or ".dll" or ".sys" => "#EF4444", // Red
            // Images
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".svg" or ".psd" => "#EC4899", // Pink
            // Audio
            ".mp3" or ".wav" or ".flac" or ".ogg" or ".aac" => "#FBBF24", // Yellow
            // Code & Data
            ".cs" or ".js" or ".ts" or ".py" or ".cpp" or ".json" or ".xml" or ".sql" => "#10B981", // Emerald
            // Documents
            ".pdf" or ".docx" or ".xlsx" or ".txt" or ".md" => "#38BDF8", // Sky
            _ => "#64748B" // Slate
        };
    }
}
