using SmartCleaner.Core.Compression;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class CompactTargetItemTests
{
    [Fact]
    public void NewItem_DefaultValues_AreSane()
    {
        var item = new CompactTargetItem();

        Assert.NotEmpty(item.Id);
        Assert.Equal("Games", item.Category);
        Assert.Equal("Не сжато", item.Status);
        Assert.Equal("LZX", item.RecommendedAlgorithm);
        Assert.False(item.IsCompressed);
        Assert.Equal(0, item.OriginalSizeBytes);
        Assert.Equal("0 B", item.OriginalSizeFormatted);
    }

    [Fact]
    public void NewItem_HasUniqueId()
    {
        var item1 = new CompactTargetItem();
        var item2 = new CompactTargetItem();
        Assert.NotEqual(item1.Id, item2.Id);
    }

    [Fact]
    public void Category_CanBeSetToAnyValue()
    {
        var item = new CompactTargetItem { Category = "Игры (Steam)" };
        Assert.Equal("Игры (Steam)", item.Category);

        item.Category = "Проекты разработки";
        Assert.Equal("Проекты разработки", item.Category);
    }

    [Fact]
    public void CompressionPercent_RoundTrip()
    {
        var item = new CompactTargetItem { CompressionPercent = 45.5 };
        Assert.Equal(45.5, item.CompressionPercent);
    }

    [Fact]
    public void SpaceSavedBytes_RoundTrip()
    {
        var item = new CompactTargetItem { SpaceSavedBytes = 1024L * 1024 * 1024 };
        Assert.Equal(1073741824, item.SpaceSavedBytes);
    }
}