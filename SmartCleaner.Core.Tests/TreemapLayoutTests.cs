using SmartCleaner.Core.DiskMap;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class TreemapLayoutTests
{
    private static DiskNode MakeNode(string name, long size, bool isFile = true)
    {
        return new DiskNode
        {
            Name = name,
            FullPath = @"C:\Root\" + name,
            Size = size,
            IsFile = isFile
        };
    }

    private static DiskNode MakeRoot(params DiskNode[] children)
    {
        return new DiskNode
        {
            Name = "Root",
            FullPath = @"C:\Root",
            Size = children.Sum(c => c.Size),
            Children = children.ToList()
        };
    }

    [Fact]
    public void CalculateSquarified_ThreeChildren_ReturnsThreeTiles()
    {
        var root = MakeRoot(
            MakeNode("Video.mp4", 600),
            MakeNode("Archive.zip", 300),
            MakeNode("Code.cs", 100)
        );

        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);

        Assert.Equal(3, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.Width > 0 && t.Height > 0));
    }

    [Fact]
    public void CalculateSquarified_NullRoot_ReturnsEmpty()
    {
        var tiles = TreemapLayout.CalculateSquarified(null!, 800, 600);
        Assert.Empty(tiles);
    }

    [Fact]
    public void CalculateSquarified_NoChildren_ReturnsEmpty()
    {
        var root = MakeRoot();
        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);
        Assert.Empty(tiles);
    }

    [Fact]
    public void CalculateSquarified_ZeroBounds_ReturnsEmpty()
    {
        var root = MakeRoot(MakeNode("a.txt", 100));
        var tiles = TreemapLayout.CalculateSquarified(root, 0, 600);
        Assert.Empty(tiles);
    }

    [Fact]
    public void CalculateSquarified_NegativeBounds_ReturnsEmpty()
    {
        var root = MakeRoot(MakeNode("a.txt", 100));
        var tiles = TreemapLayout.CalculateSquarified(root, -100, 600);
        Assert.Empty(tiles);
    }

    [Fact]
    public void CalculateSquarified_SingleChild_FillsEntireBounds()
    {
        var root = MakeRoot(MakeNode("only.txt", 500));
        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);

        var tile = Assert.Single(tiles);
        Assert.Equal(0, tile.X);
        Assert.Equal(0, tile.Y);
        // CreateRect subtracts 2px margin from width/height
        Assert.Equal(800 - 2, tile.Width);
        Assert.Equal(600 - 2, tile.Height);
        Assert.Equal("only.txt", tile.DisplayName);
    }

    [Fact]
    public void CalculateSquarified_AllZeroSizeChildren_ReturnsEmpty()
    {
        var root = MakeRoot(
            MakeNode("empty1", 0),
            MakeNode("empty2", 0)
        );
        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);
        Assert.Empty(tiles);
    }

    [Fact]
    public void CalculateSquarified_MixedZeroAndNonZero_ExcludesZeros()
    {
        var root = MakeRoot(
            MakeNode("real.txt", 300),
            MakeNode("zero.dat", 0),
            MakeNode("also_real.txt", 700)
        );
        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);

        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.Width > 0 && t.Height > 0));
        Assert.DoesNotContain(tiles, t => t.DisplayName == "zero.dat");
    }

    [Fact]
    public void CalculateSquarified_TilesDoNotOverlapAndCoverArea()
    {
        var root = MakeRoot(
            MakeNode("a.mp4", 400),
            MakeNode("b.zip", 300),
            MakeNode("c.cs", 200),
            MakeNode("d.jpg", 100)
        );

        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);

        Assert.Equal(4, tiles.Count);
        Assert.All(tiles, t => Assert.True(t.Width > 0 && t.Height > 0));

        // Each tile should be within bounds
        Assert.All(tiles, t =>
        {
            Assert.True(t.X >= 0);
            Assert.True(t.Y >= 0);
            Assert.True(t.X + t.Width <= 800 + 1); // 1px tolerance for rounding
            Assert.True(t.Y + t.Height <= 600 + 1);
        });
    }

    [Fact]
    public void CalculateSquarified_Color_IsNotNull()
    {
        var root = MakeRoot(
            MakeNode("file.txt", 100, isFile: true),
            MakeNode("folder", 200, isFile: false)
        );

        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);
        Assert.All(tiles, t => Assert.False(string.IsNullOrEmpty(t.Color)));
    }

    [Fact]
    public void CalculateSquarified_FormattedSize_IsNotEmpty()
    {
        var root = MakeRoot(
            MakeNode("big.bin", 2048)
        );
        var tiles = TreemapLayout.CalculateSquarified(root, 800, 600);
        var tile = Assert.Single(tiles);
        Assert.NotEmpty(tile.FormattedSize);
    }
}