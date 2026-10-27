using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 8 (ROADMAP v3.1, фаза M): контракты сборки дерева файлов из
/// MFT-записей — привязка детей по родителю с проверкой sequence, слияние
/// extension-записей, жёсткие связи (один inode = один узел, размер не
/// размножается), сироты и циклы, итоговые размеры снизу вверх.
/// Записи строятся через билдер → fixup → parse (контракт ридера).
/// </summary>
public class MftTreeBuilderTests
{
    /// <summary>Собирает живую запись с именем и опциональными данными.</summary>
    private static MftRecord Record(
        uint number,
        string? name,
        MftRecordFlags flags = MftRecordFlags.InUse,
        ulong parent = 5,
        ushort parentSeq = 7,
        ulong allocatedSize = 0,
        ulong realSize = 0,
        ushort linkCount = 1,
        ulong? baseRecord = null,
        ushort sequence = 7,
        bool resident = false)
    {
        var builder = new MftRecordBuilder(recordNumber: number, sequence: sequence)
        {
            Flags = flags,
            LinkCount = linkCount,
            BaseRecordNumber = baseRecord,
        };
        if (name != null)
            builder.AddFileName(name, MftNamespace.Win32, parent, parentSeq);
        if (resident)
            builder.AddResidentData(new byte[(int)realSize]);
        else if (allocatedSize > 0 || realSize > 0)
            builder.AddNonResidentData(allocatedSize, realSize);

        byte[] bytes = builder.Build();
        Assert.Null(MftParser.ApplyFixups(bytes, builder.BytesPerSector));
        return MftParser.ParseRecord(bytes, builder.RecordSize);
    }

    private static MftRecord Root()
        => Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory);

    [Fact]
    public void Build_RootWithFile_AttachesChildAndSizes()
    {
        MftTree tree = MftTreeBuilder.Build([
            Record(42, "a.txt", allocatedSize: 8192, realSize: 5000),
            Root(),
        ]);

        Assert.NotNull(tree.Root);
        Assert.Equal(5UL, tree.Root.RecordNumber);
        MftTreeNode file = Assert.Single(tree.Root.Children);
        Assert.Equal("a.txt", file.FileName);
        Assert.Equal(tree.Root, file.Parent);
        Assert.False(file.IsDirectory);
        Assert.Equal(8192UL, file.Size); // allocated > 0 → выделенный размер
        Assert.Equal(8192UL, tree.Root.Size);
    }

    [Fact]
    public void Build_NestedDirectories_AggregateSizes()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "d", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(43, "f1", parent: 42, allocatedSize: 4096),
            Record(44, "f2", parent: 42, allocatedSize: 8192),
        ]);

        MftTreeNode dir = tree.Nodes[42];
        Assert.True(dir.IsDirectory);
        Assert.Equal(2, dir.Children.Count);
        Assert.Equal(12288UL, dir.Size); // Σ детей
        Assert.Equal(12288UL, tree.Root!.Size);
    }

    [Fact]
    public void Build_HardLink_SingleNodeSizeNotDuplicated()
    {
        // жёсткая связь: один inode = одна запись = один узел; размер считается один раз
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "a.txt", allocatedSize: 1000, linkCount: 2),
        ]);

        MftTreeNode file = Assert.Single(tree.Root!.Children);
        Assert.Equal(1, file.ExtraLinks);
        Assert.Equal(1000UL, file.Size);
        Assert.Equal(1000UL, tree.Root.Size);
    }

    [Fact]
    public void Build_ParentMissing_FileOrphaned()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(43, "f", parent: 999),
        ]);

        Assert.Contains(tree.Orphans, n => n.RecordNumber == 43);
        Assert.Empty(tree.Root!.Children);
    }

    [Fact]
    public void Build_RecycledParent_SequenceMismatch_OrphansChild()
    {
        // родитель 42 переработан (sequence 8, а ссылка ждёт 7) — ребёнок сирота
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "d", MftRecordFlags.InUse | MftRecordFlags.Directory, sequence: 8),
            Record(43, "f", parent: 42, parentSeq: 7),
        ]);

        Assert.Contains(tree.Root!.Children, n => n.RecordNumber == 42);
        Assert.Contains(tree.Orphans, n => n.RecordNumber == 43);
    }

    [Fact]
    public void Build_ExtensionRecord_MergesSizesIntoBase()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "big.bin", allocatedSize: 1000, realSize: 800),
            Record(43, null, allocatedSize: 2000, realSize: 1500, baseRecord: 42),
        ]);

        MftTreeNode file = tree.Nodes[42];
        Assert.Equal(3000UL, file.AllocatedSize);
        Assert.Equal(2300UL, file.RealSize);
        Assert.Equal(3000UL, file.Size);
        Assert.False(tree.Nodes.ContainsKey(43)); // extension слился — узла нет
        Assert.Equal(3000UL, tree.Root!.Size);
    }

    [Fact]
    public void Build_ExtensionWithoutBase_GoesToOrphans()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(43, null, allocatedSize: 100, baseRecord: 42), // базы 42 нет во входе
        ]);

        MftTreeNode orphan = tree.Orphans.Single(n => n.RecordNumber == 43);
        Assert.Equal(100UL, orphan.Size); // данные не теряются
    }

    [Fact]
    public void Build_DeletedRecord_Excluded()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "deleted.txt", flags: MftRecordFlags.None, allocatedSize: 100),
        ]);

        Assert.False(tree.Nodes.ContainsKey(42));
        Assert.Empty(tree.Root!.Children);
    }

    [Fact]
    public void Build_RootMissing_EverythingOrphaned()
    {
        MftTree tree = MftTreeBuilder.Build([
            Record(42, "f", parent: 5),
        ]);

        Assert.Null(tree.Root);
        Assert.Contains(tree.Orphans, n => n.RecordNumber == 42);
    }

    [Fact]
    public void Build_Cycle_DoesNotHang_OrphansEverything()
    {
        MftTree tree = MftTreeBuilder.Build([
            Record(10, "a", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: 20, parentSeq: 8, sequence: 7),
            Record(20, "b", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: 10, parentSeq: 7, sequence: 8),
        ]);

        Assert.Null(tree.Root);
        Assert.Contains(tree.Orphans, n => n.RecordNumber == 10);
        Assert.Contains(tree.Orphans, n => n.RecordNumber == 20);
    }

    [Fact]
    public void Build_DuplicateRecordNumber_FirstOccurrenceWins()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "first"),
            Record(42, "second"),
        ]);

        Assert.Equal("first", tree.Nodes[42].FileName);
        Assert.Single(tree.Root!.Children);
    }

    [Fact]
    public void Build_ResidentData_SizeFallsBackToReal()
    {
        MftTree tree = MftTreeBuilder.Build([
            Root(),
            Record(42, "small.txt", realSize: 100, resident: true),
        ]);

        Assert.Equal(100UL, tree.Nodes[42].Size);
        Assert.Equal(100UL, tree.Root!.Size);
    }

    [Fact]
    public void Build_EmptyInput_EmptyTree()
    {
        MftTree tree = MftTreeBuilder.Build([]);

        Assert.Null(tree.Root);
        Assert.Empty(tree.Nodes);
        Assert.Empty(tree.Orphans);
    }
}
