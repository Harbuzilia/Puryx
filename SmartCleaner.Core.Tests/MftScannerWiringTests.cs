using System.Globalization;
using SmartCleaner.Core.DiskMap;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Mft;
using SmartCleaner.Core.Scanning.Scanners;
using SmartCleaner.Core.Services;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 9 (ROADMAP v3.1, фаза M): интеграция MFT-пути в сканеры —
/// DiskUsageScanner/LargeFilesEngine/DiskMapEngine получают MftDiskScanner
/// и используют дерево $MFT вместо обхода каталогов; путь корня вне дерева
/// → честный фолбэк на обход с причиной в progress (ключи Mft_Status_Mode_*).
/// Фейковый ридер описывает цепочку сегментов temp-пути; файлы/папки,
/// реально существующие на диске, но отсутствующие в дереве, доказывают,
/// что обход не выполнялся.
/// </summary>
public class MftScannerWiringTests : IDisposable
{
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public MftScannerWiringTests()
    {
        Localizer.Culture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        Localizer.Culture = _originalLocalizerCulture;
    }

    // ---------- фикстуры ----------

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
    /// Записи-цепочка каталогов от корня тома до directoryPath (все сегменты —
    /// каталоги, родительство по цепочке) + корневая запись ".": FindSubtree
    /// навигируется по ней как по реальному дереву тома.
    /// </summary>
    private static (List<MftRecord> Records, ulong TailParent) ChainRecords(string directoryPath)
    {
        var records = new List<MftRecord>
        {
            Record(5, ".", MftRecordFlags.InUse | MftRecordFlags.Directory)
        };

        string root = Path.GetPathRoot(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
        string remainder = directoryPath.Substring(root.Length);
        ulong parent = 5;
        uint number = 100;
        foreach (string segment in remainder.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            records.Add(Record(number, segment, MftRecordFlags.InUse | MftRecordFlags.Directory, parent: parent));
            parent = number;
            number++;
        }

        return (records, parent);
    }

    /// <summary>Мок ридера тома: синтетические записи, всегда читаемый.</summary>
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

    /// <summary>Фасад с мок-швами: ридер тома подменён, NTFS всегда true.</summary>
    private sealed class StubMftScanner(IEnumerable<MftRecord> records) : MftDiskScanner
    {
        private readonly FakeVolumeReader _reader = new(records);

        protected override IMftVolumeReader? OpenVolumeReader(char letter) => _reader;

        protected override bool IsNtfsVolume(char letter) => true;
    }

    /// <summary>Собирает строки progress для ассертов на индикатор режима.</summary>
    private sealed class ProgressRecorder : IProgress<string>
    {
        public List<string> Lines { get; } = [];

        public void Report(string value) => Lines.Add(value);
    }

    /// <summary>Уникальный temp-каталог (GetFullPath: TMP-обход песочницы даёт относительный путь).</summary>
    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetFullPath(Path.GetTempPath()), "sc_mft_wiring", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    // ---------- DiskUsageScanner ----------

    [Fact]
    public async Task DiskUsageScanner_MftTreeUsed_WalkSkipped()
    {
        string tempRoot = CreateTempRoot();
        string mftBig = Path.Combine(tempRoot, "mft_big");
        string realWalk = Path.Combine(tempRoot, "real_walk");
        Directory.CreateDirectory(mftBig);
        Directory.CreateDirectory(realWalk);
        File.WriteAllBytes(Path.Combine(mftBig, "probe.bin"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(realWalk, "probe.bin"), [4, 5, 6]);

        var (records, tail) = ChainRecords(tempRoot);
        records.Add(Record(200, "mft_big", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: tail));
        records.Add(Record(201, "big.dat", parent: 200, allocatedSize: 5_000_000));

        var scanner = new DiskUsageScanner(
            new AllowAllSafetyService(),
            new InMemoryConfigService([tempRoot]),
            new StubMftScanner(records))
        {
            MinInsightBytes = 1
        };
        var progress = new ProgressRecorder();

        var result = await scanner.ScanAsync(progress, CancellationToken.None);

        Assert.Contains(result.Items, i => i.Path.Equals(mftBig, StringComparison.OrdinalIgnoreCase));
        // real_walk есть на диске, но отсутствует в дереве → обход не выполнялся
        Assert.DoesNotContain(result.Items,
            i => i.Path.StartsWith(realWalk, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Localizer.GetString("Mft_Status_Mode_Mft"), progress.Lines);
    }

    [Fact]
    public async Task DiskUsageScanner_PathNotInTree_FallsBackToWalk()
    {
        string tempRoot = CreateTempRoot();
        string realWalk = Path.Combine(tempRoot, "real_walk");
        Directory.CreateDirectory(realWalk);
        File.WriteAllBytes(Path.Combine(realWalk, "probe.bin"), [4, 5, 6]);

        // дерево только с корнем тома: скан успешен, но поддерево temp-пути не найдено
        var (records, _) = ChainRecords(tempRoot);
        var rootOnlyTree = records.Take(1).ToList();

        var scanner = new DiskUsageScanner(
            new AllowAllSafetyService(),
            new InMemoryConfigService([tempRoot]),
            new StubMftScanner(rootOnlyTree))
        {
            MinInsightBytes = 1
        };
        var progress = new ProgressRecorder();

        var result = await scanner.ScanAsync(progress, CancellationToken.None);

        // честный фолбэк: обход нашёл реальную папку
        Assert.Contains(result.Items,
            i => i.Path.StartsWith(realWalk, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            Localizer.GetString("Mft_Status_Mode_Walk", Localizer.GetString("Mft_Fallback_PathNotFound")),
            progress.Lines);
    }

    // ---------- LargeFilesEngine ----------

    [Fact]
    public async Task LargeFilesEngine_MftTreeUsed_WalkSkipped()
    {
        string tempRoot = CreateTempRoot();
        string mftFile = Path.Combine(tempRoot, "mft_big.bin");
        string realFile = Path.Combine(tempRoot, "real_walk.bin");
        File.WriteAllBytes(mftFile, [1, 2, 3]); // 3 байта на диске против 5_000_000 в дереве
        File.WriteAllBytes(realFile, [4, 5, 6]);

        var (records, tail) = ChainRecords(tempRoot);
        records.Add(Record(200, "mft_big.bin", parent: tail, allocatedSize: 5_000_000));

        var engine = new LargeFiles.LargeFilesEngine(new StubMftScanner(records));
        var progress = new ProgressRecorder();

        List<LargeFiles.LargeFileInfo> files = await engine.ScanAsync(
            tempRoot, topN: 10, minSizeBytes: 1, progress, CancellationToken.None);

        // real_walk.bin есть на диске, но не в дереве: единственный результат — из дерева
        var file = Assert.Single(files);
        Assert.Equal(mftFile, file.Path, StringComparer.OrdinalIgnoreCase);
        // размер из дерева (не 3 байта с диска) — доказательство MFT-пути
        Assert.Equal(5_000_000L, file.Size);
        // best-effort обогащение Modified для финальных top-N: файл существует
        Assert.True(file.Modified != default);
        Assert.Contains(Localizer.GetString("Mft_Status_Mode_Mft"), progress.Lines);
    }

    // ---------- DiskMapEngine ----------

    [Fact]
    public async Task DiskMapEngine_MftTreeUsed_WalkSkipped()
    {
        string tempRoot = CreateTempRoot();
        string mftDir = Path.Combine(tempRoot, "mft_dir");
        string realDir = Path.Combine(tempRoot, "real_dir");
        Directory.CreateDirectory(mftDir);
        Directory.CreateDirectory(realDir);
        File.WriteAllBytes(Path.Combine(mftDir, "from_tree.dat"), [1]);
        File.WriteAllBytes(Path.Combine(realDir, "real.dat"), [1]);

        var (records, tail) = ChainRecords(tempRoot);
        records.Add(Record(200, "mft_dir", MftRecordFlags.InUse | MftRecordFlags.Directory, parent: tail));
        records.Add(Record(201, "from_tree.dat", parent: 200, allocatedSize: 4000));

        var engine = new DiskMapEngine(new StubMftScanner(records));
        var progress = new ProgressRecorder();

        DiskNode root = await engine.ScanAsync(tempRoot, maxDepth: 3, progress, CancellationToken.None);

        // mft_dir из дерева с размером записи (не 1 байт с диска) — MFT-путь
        DiskNode? mftNode = root.Children.FirstOrDefault(
            c => string.Equals(c.Name, "mft_dir", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(mftNode);
        Assert.Equal(4000L, mftNode!.Size);
        // real_dir есть на диске, но не в дереве → обход не выполнялся
        Assert.DoesNotContain(root.Children,
            c => string.Equals(c.Name, "real_dir", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Localizer.GetString("Mft_Status_Mode_Mft"), progress.Lines);
    }
}
