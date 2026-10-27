using System.Globalization;
using SmartCleaner.Core.DiskMap;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Mft;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 9 (ROADMAP v3.1, фаза M): контракты фасада MftDiskScanner —
/// pure-конверсии дерева MFT в модели сканеров (LargeFiles/DiskUsage/
/// DiskMap) на синтетике через билдер записей, выбор режима MFT
/// (Auto/On/Off) с мок-ридером и навигация поддерева по абсолютному пути.
/// Локализуемые строки — при закреплённой инвариантной культуре.
/// </summary>
public class MftDiskScannerTests : IDisposable
{
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public MftDiskScannerTests()
    {
        Localizer.Culture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        Localizer.Culture = _originalLocalizerCulture;
    }

    // ---------- фикстуры: синтетическое дерево тома ----------

    /// <summary>Собирает живую запись с именем и опциональными данными (как MftTreeBuilderTests).</summary>
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

    /// <summary>
    /// Синтетическое дерево: root(5) → Users{big1 5000, big2 3000, small 10,
    /// hard.bin 1000 (hardlink)}, Cache{deep 4000, Inner{inner 2000}}.
    /// Итоги: Users 9010, Cache 6000, root 15001.
    /// </summary>
    private static MftTree BuildSampleTree()
    {
        return MftTreeBuilder.Build(
        [
            Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(42, "Users", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(43, "big1.dat", parent: 42, allocatedSize: 5000),
            Record(44, "big2.dat", parent: 42, allocatedSize: 3000),
            Record(45, "small.dat", parent: 42, allocatedSize: 10),
            Record(48, "hard.bin", parent: 42, allocatedSize: 1000, linkCount: 3),
            Record(46, "Cache", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(47, "deep.dat", parent: 46, allocatedSize: 4000),
            Record(49, "Inner", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: 46),
            Record(50, "inner.dat", parent: 49, allocatedSize: 2000),
        ]);
    }

    // ---------- ToLargeFiles ----------

    [Fact]
    public void ToLargeFiles_TopFilesBySize_WithPathsAndFields()
    {
        MftTree tree = BuildSampleTree();

        List<LargeFiles.LargeFileInfo> files = MftDiskScanner.ToLargeFiles(
            tree.Root!, @"C:\", minSize: 1, maxCount: 3);

        Assert.Equal(3, files.Count);
        Assert.Equal(@"C:\Users\big1.dat", files[0].Path);
        Assert.Equal(5000L, files[0].Size);
        Assert.Equal(@"C:\Cache\deep.dat", files[1].Path);
        Assert.Equal(4000L, files[1].Size);
        Assert.Equal(@"C:\Users\big2.dat", files[2].Path);
        Assert.Equal(3000L, files[2].Size);

        // Поля как у walk-режима LargeFilesEngine
        Assert.Equal("big1.dat", files[0].Name);
        Assert.Equal(@"C:\Users", files[0].Directory);
        Assert.Equal(".dat", files[0].Extension);
        Assert.Equal(default(DateTime), files[0].Modified);
    }

    [Fact]
    public void ToLargeFiles_MinSize_FiltersOutSmallFiles()
    {
        MftTree tree = BuildSampleTree();

        List<LargeFiles.LargeFileInfo> files = MftDiskScanner.ToLargeFiles(
            tree.Root!, @"C:\", minSize: 2500, maxCount: 10);

        // small(10), hard(1000), inner(2000) отфильтрованы порогом
        Assert.Equal([@"C:\Users\big1.dat", @"C:\Cache\deep.dat", @"C:\Users\big2.dat"],
            files.Select(f => f.Path).ToList());
    }

    [Fact]
    public void ToLargeFiles_EqualSizes_TieBreakByPath()
    {
        MftTree tree = MftTreeBuilder.Build(
        [
            Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(42, "b.txt", allocatedSize: 1000),
            Record(43, "a.txt", allocatedSize: 1000),
        ]);

        List<LargeFiles.LargeFileInfo> files = MftDiskScanner.ToLargeFiles(
            tree.Root!, @"C:\", minSize: 1, maxCount: 10);

        Assert.Equal([@"C:\a.txt", @"C:\b.txt"], files.Select(f => f.Path).ToList());
    }

    [Fact]
    public void ToLargeFiles_HardLink_SizeCountedOnce()
    {
        MftTree tree = MftTreeBuilder.Build(
        [
            Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(42, "hard.bin", allocatedSize: 1000, linkCount: 3),
        ]);

        List<LargeFiles.LargeFileInfo> files = MftDiskScanner.ToLargeFiles(
            tree.Root!, @"C:\", minSize: 1, maxCount: 10);

        // один inode = один файл, размер НЕ размножается числом связей
        var file = Assert.Single(files);
        Assert.Equal(1000L, file.Size);
    }

    // ---------- ToFolderItems ----------

    [Fact]
    public void ToFolderItems_FoldersAboveThreshold_AsDiskUsageItems()
    {
        MftTree tree = BuildSampleTree();

        List<ScannedItem> items = MftDiskScanner.ToFolderItems(
            tree.Root!, @"C:\", minBytes: 6000);

        // Users(9010) и Cache(6000) выше порога; Inner(2000) и файлы — нет
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.True(i.IsDirectory));

        ScannedItem users = Assert.Single(items, i => i.Path == @"C:\Users");
        Assert.Equal(9010L, users.Size);
        Assert.Equal(default(DateTime), users.LastAccess);
        Assert.Equal(RiskCategory.UserData, users.Risk); // "c:\users" — без кэш-паттернов
        Assert.False(users.IsSelected);
        Assert.Contains(SizeFormatter.Format(9010), users.Description);
        Assert.Equal(Localizer.GetString("Scanner_DiskUsage_Root", @"C:\"), users.ParentApp);
    }

    [Fact]
    public void ToFolderItems_CachePath_ClassifiedAsPerformanceCache()
    {
        MftTree tree = BuildSampleTree();

        List<ScannedItem> items = MftDiskScanner.ToFolderItems(
            tree.Root!, @"C:\", minBytes: 6000);

        // "c:\cache" совпадает с кэш-паттерном DiskUsageScanner
        ScannedItem cache = Assert.Single(items, i => i.Path == @"C:\Cache");
        Assert.Equal(RiskCategory.PerformanceCache, cache.Risk);
        Assert.Equal(6000L, cache.Size);
    }

    [Fact]
    public void ToFolderItems_RootSubtreeNotEmitted()
    {
        MftTree tree = BuildSampleTree();

        // порог 0: все папки, но сам корень поддерева не выдаётся
        List<ScannedItem> items = MftDiskScanner.ToFolderItems(
            tree.Root!, @"C:\", minBytes: 0);

        Assert.Equal(3, items.Count); // Users, Cache, Inner
        Assert.DoesNotContain(items, i => i.Path == @"C:\");
    }

    [Fact]
    public void ToFolderItems_ThresholdAboveAll_Empty()
    {
        MftTree tree = BuildSampleTree();

        List<ScannedItem> items = MftDiskScanner.ToFolderItems(
            tree.Root!, @"C:\", minBytes: 20000);

        Assert.Empty(items);
    }

    // ---------- ToTreemapInput ----------

    [Fact]
    public void ToTreemapInput_FullTree_MirrorsDiskMapShape()
    {
        MftTree tree = BuildSampleTree();

        DiskNode root = MftDiskScanner.ToTreemapInput(tree.Root!, @"C:\", maxDepth: 0);

        Assert.Equal("C:", root.Name); // FileName "." → сегмент корневого пути
        Assert.Equal(@"C:\", root.FullPath);
        Assert.False(root.IsFile);
        Assert.Equal(0, root.Depth);
        Assert.Equal(15001L, root.Size);

        // дети корня отсортированы по размеру desc: Users(9010), Cache(6000)
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("Users", root.Children[0].Name);
        Assert.Equal(9010L, root.Children[0].Size);
        Assert.Equal(1, root.Children[0].Depth);
        Assert.Equal("Cache", root.Children[1].Name);
        Assert.Equal(6000L, root.Children[1].Size);

        // файлы на каждом уровне + спуск в подпапки; проценты от родителя
        Assert.Equal(4, root.Children[0].Children.Count); // big1, big2, hard, small
        Assert.Equal("big1.dat", root.Children[0].Children[0].Name);
        Assert.True(root.Children[0].Children[0].IsFile);
        Assert.Equal(2, root.Children[0].Children[0].Depth);

        DiskNode cache = root.Children[1];
        Assert.Equal([("deep.dat", 4000L), ("Inner", 2000L)],
            cache.Children.Select(c => (c.Name, c.Size)).ToList());
        Assert.Equal(2000L, Assert.Single(cache.Children[1].Children).Size);

        Assert.Equal(100.0, root.Children[0].Percentage, 3);
        Assert.Equal((double)9010 / 15001 * 100, root.Children[0].Percentage, 3);
        Assert.Equal((double)5000 / 9010 * 100, root.Children[0].Children[0].Percentage, 3);
    }

    [Fact]
    public void ToTreemapInput_DepthLimit_OmitsSubdirectories()
    {
        MftTree tree = BuildSampleTree();

        // maxDepth 1: узлы глубже не спускаются — Inner целиком отсутствует
        DiskNode root = MftDiskScanner.ToTreemapInput(tree.Root!, @"C:\", maxDepth: 1);

        DiskNode cache = Assert.Single(root.Children, c => c.Name == "Cache");
        Assert.Equal(4000L, cache.Size); // только файлы своего уровня
        Assert.Equal("deep.dat", Assert.Single(cache.Children).Name);
        Assert.Equal(13001L, root.Size); // 9010 + 4000 (без Inner)
    }

    [Fact]
    public void ToTreemapInput_NamedRoot_UsesNodeName()
    {
        MftTree tree = BuildSampleTree();

        // поддерево по пути: имя корневого узла — имя папки
        DiskNode users = MftDiskScanner.ToTreemapInput(tree.Nodes[42], @"C:\Users", maxDepth: 0);

        Assert.Equal("Users", users.Name);
        Assert.Equal(@"C:\Users", users.FullPath);
        Assert.Equal(9010L, users.Size);
        Assert.Equal(4, users.Children.Count);
        Assert.All(users.Children, c => Assert.Equal(@"C:\Users\" + c.Name, c.FullPath));
    }

    // ---------- FindSubtree ----------

    [Fact]
    public void FindSubtree_ResolvesNestedPath_CaseInsensitive()
    {
        MftTree tree = BuildSampleTree();

        MftTreeNode? node = MftDiskScanner.FindSubtree(tree, @"C:\", @"C:\Cache\Inner");

        Assert.NotNull(node);
        Assert.Equal(49UL, node!.RecordNumber);
        Assert.True(node.IsDirectory);

        // регистронезависимо (как Windows-пути)
        Assert.Equal(node, MftDiskScanner.FindSubtree(tree, @"C:\", @"c:\cache\INNER"));
    }

    [Fact]
    public void FindSubtree_VolumeRoot_ReturnsRootNode()
    {
        MftTree tree = BuildSampleTree();

        Assert.Equal(tree.Root, MftDiskScanner.FindSubtree(tree, @"C:\", @"C:\"));
        Assert.Equal(tree.Root, MftDiskScanner.FindSubtree(tree, @"C:\", @"C:"));
    }

    [Fact]
    public void FindSubtree_MissingSegment_ReturnsNull()
    {
        MftTree tree = BuildSampleTree();

        Assert.Null(MftDiskScanner.FindSubtree(tree, @"C:\", @"C:\Windows"));
        Assert.Null(MftDiskScanner.FindSubtree(tree, @"C:\", @"C:\Cache\Missing"));
        Assert.Null(MftDiskScanner.FindSubtree(tree, @"C:\", @"D:\Users")); // вне тома
    }

    [Fact]
    public void FindSubtree_FileSegment_ReturnsNull()
    {
        MftTree tree = BuildSampleTree();

        // сегменты пути — только каталоги: файл с тем же именем не матчится
        Assert.Null(MftDiskScanner.FindSubtree(tree, @"C:\", @"C:\Users\big1.dat"));
    }

    // ---------- TryScanDrive: выбор режима ----------

    /// <summary>Мок ридера тома: синтетические записи, управляемый отказ.</summary>
    private sealed class FakeVolumeReader(IEnumerable<MftRecord> records) : IMftVolumeReader
    {
        private readonly List<MftRecord> _records = [.. records];

        public bool CanRead { get; set; } = true;
        public string? FailureReason { get; set; }
        public MftReaderFailure FailureKind { get; set; } = MftReaderFailure.None;
        public NtfsBootSector Boot => default;
        public int FileRecordSize => 1024;
        public long RecordCapacity => _records.Count;

        public IEnumerable<MftRawRecord> EnumerateRawRecords(CancellationToken ct = default)
            => throw new NotSupportedException("fake reads parsed records only");

        public IEnumerable<MftRecord> EnumerateRecords(CancellationToken ct = default)
            => _records;

        public void Dispose() { }
    }

    /// <summary>Фасад с мок-швами: ридер и NTFS-проверка подменяются, счётчик открытий.</summary>
    private sealed class FakeMftDiskScanner : MftDiskScanner
    {
        public FakeMftDiskScanner(ScanSettingsConfig? settings = null)
            : base(new InMemoryConfigService([]) { ScanSettings = settings })
        {
        }

        public FakeVolumeReader? Reader { get; set; }
        public bool Ntfs { get; set; } = true;
        public int OpenCalls { get; private set; }

        protected override IMftVolumeReader? OpenVolumeReader(char letter)
        {
            OpenCalls++;
            return Reader;
        }

        protected override bool IsNtfsVolume(char letter) => Ntfs;
    }

    [Fact]
    public void TryScanDrive_OffSetting_FailsDisabled_WithoutOpeningVolume()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Off" })
        {
            Reader = new FakeVolumeReader(BuildSampleTreeNodes())
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.False(result.Success);
        Assert.Equal(MftScanFailureKind.Disabled, result.FailureKind);
        Assert.False(string.IsNullOrEmpty(result.FailureText));
        Assert.Equal(0, scanner.OpenCalls); // том не открывается вовсе
    }

    [Fact]
    public void TryScanDrive_AutoNonNtfs_FailsNotNtfs_WithoutOpeningVolume()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Auto" })
        {
            Ntfs = false
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.False(result.Success);
        Assert.Equal(MftScanFailureKind.NotNtfs, result.FailureKind);
        Assert.False(string.IsNullOrEmpty(result.FailureText));
        Assert.Equal(0, scanner.OpenCalls); // дешёвая проверка до открытия тома
    }

    [Fact]
    public void TryScanDrive_VolumeNotOpened_FailsNotNtfs()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Auto" })
        {
            Reader = null // том принципиально не открылся (не NTFS/нет диска)
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.False(result.Success);
        Assert.Equal(MftScanFailureKind.NotNtfs, result.FailureKind);
    }

    [Fact]
    public void TryScanDrive_AccessDenied_FailsRights_WithAdminHint()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Auto" })
        {
            Reader = new FakeVolumeReader([])
            {
                CanRead = false,
                FailureReason = "отказано в доступе",
                FailureKind = MftReaderFailure.AccessDenied
            }
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.False(result.Success);
        Assert.Equal(MftScanFailureKind.AccessDenied, result.FailureKind);
        // подсказка «запустите от имени администратора» (инвариантная культура = EN)
        Assert.NotNull(result.FailureText);
        Assert.Contains("administrator", result.FailureText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryScanDrive_VolumeError_FailsWithReasonInText()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Auto" })
        {
            Reader = new FakeVolumeReader([])
            {
                CanRead = false,
                FailureReason = "запись $MFT нечитаема",
                FailureKind = MftReaderFailure.VolumeError
            }
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.False(result.Success);
        Assert.Equal(MftScanFailureKind.VolumeError, result.FailureKind);
        Assert.Contains("запись $MFT нечитаема", result.FailureText);
    }

    [Fact]
    public void TryScanDrive_OnSetting_SkipsNtfsCheck_AttemptsAnyway()
    {
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "On" })
        {
            Ntfs = false, // On = всегда попытка, NTFS-префектор пропускается
            Reader = new FakeVolumeReader(BuildSampleTreeNodes())
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.True(result.Success);
        Assert.Equal(1, scanner.OpenCalls);
    }

    [Fact]
    public void TryScanDrive_DefaultSetting_IsAuto()
    {
        // настройки нет → Auto: NTFS + читаемый ридер → MFT
        var scanner = new FakeMftDiskScanner(null) { Reader = new FakeVolumeReader(BuildSampleTreeNodes()) };

        MftScanResult result = scanner.TryScanDrive(@"C:\");

        Assert.True(result.Success);
    }

    [Fact]
    public void TryScanDrive_Success_ReturnsTreeAndStats()
    {
        List<MftRecord> records = BuildSampleTreeNodes();
        var scanner = new FakeMftDiskScanner(new ScanSettingsConfig { MftScanMode = "Auto" })
        {
            Reader = new FakeVolumeReader(records)
        };

        MftScanResult result = scanner.TryScanDrive(@"C:\Users");

        Assert.True(result.Success);
        Assert.Equal("MFT", result.Mode);
        Assert.Equal(MftScanFailureKind.None, result.FailureKind);
        Assert.Null(result.FailureText);
        Assert.NotNull(result.Tree);
        Assert.NotNull(result.Tree.Root);
        Assert.Equal(15001UL, result.Tree.Root.Size);
        Assert.Equal((long)records.Count, result.RecordCount); // все записи посчитаны
        Assert.True(result.Elapsed >= TimeSpan.Zero);
    }

    /// <summary>Записи синтетического дерева в виде списка (для мок-ридера).</summary>
    private static List<MftRecord> BuildSampleTreeNodes()
    {
        return
        [
            Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(42, "Users", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(43, "big1.dat", parent: 42, allocatedSize: 5000),
            Record(44, "big2.dat", parent: 42, allocatedSize: 3000),
            Record(45, "small.dat", parent: 42, allocatedSize: 10),
            Record(48, "hard.bin", parent: 42, allocatedSize: 1000, linkCount: 3),
            Record(46, "Cache", MftRecordFlags.InUse | MftRecordFlags.Directory),
            Record(47, "deep.dat", parent: 46, allocatedSize: 4000),
            Record(49, "Inner", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: 46),
            Record(50, "inner.dat", parent: 49, allocatedSize: 2000),
        ];
    }
}
