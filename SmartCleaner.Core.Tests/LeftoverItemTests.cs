using SmartCleaner.Core.Uninstaller;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class LeftoverItemTests
{
    [Fact]
    public void TypeName_Folder_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.Folder };
        Assert.Equal("Папка", item.TypeName);
    }

    [Fact]
    public void TypeName_File_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.File };
        Assert.Equal("Файл", item.TypeName);
    }

    [Fact]
    public void TypeName_RegistryKey_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.RegistryKey };
        Assert.Equal("Раздел реестра", item.TypeName);
    }

    [Fact]
    public void TypeName_RegistryValue_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.RegistryValue };
        Assert.Equal("Параметр реестра", item.TypeName);
    }

    [Fact]
    public void TypeName_ScheduledTask_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.ScheduledTask };
        Assert.Equal("Задача планировщика", item.TypeName);
    }

    [Fact]
    public void TypeName_Service_ReturnsRussianName()
    {
        var item = new LeftoverItem { Type = LeftoverType.Service };
        Assert.Equal("Служба", item.TypeName);
    }

    [Fact]
    public void TypeName_Default_ReturnsElement()
    {
        var item = new LeftoverItem { Type = (LeftoverType)999 };
        Assert.Equal("Элемент", item.TypeName);
    }

    [Fact]
    public void NewItem_IsSelectedByDefault()
    {
        var item = new LeftoverItem();
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void NewItem_HasUniqueId()
    {
        var item1 = new LeftoverItem();
        var item2 = new LeftoverItem();
        Assert.NotEqual(item1.Id, item2.Id);
    }

    [Fact]
    public void NewItem_SizeFormattedDefaultsToDash()
    {
        var item = new LeftoverItem();
        Assert.Equal("-", item.SizeFormatted);
    }

    [Fact]
    public void NewItem_SizeBytesDefaultsToZero()
    {
        var item = new LeftoverItem();
        Assert.Equal(0, item.SizeBytes);
    }

    [Fact]
    public void Path_And_Description_RoundTrip()
    {
        var item = new LeftoverItem
        {
            Path = @"C:\Users\Test\AppData\Roaming\SomeApp",
            Description = "Остаточная папка в AppData",
            SizeBytes = 150000
        };

        Assert.Equal(@"C:\Users\Test\AppData\Roaming\SomeApp", item.Path);
        Assert.Equal("Остаточная папка в AppData", item.Description);
        Assert.Equal(150000, item.SizeBytes);
    }
}