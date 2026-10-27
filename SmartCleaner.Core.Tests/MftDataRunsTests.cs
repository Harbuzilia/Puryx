using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 8 (ROADMAP v3.1, фаза M): контракты поиска run-list неименованного
/// нерезидентного $DATA в (уже зафиксапленной) MFT-записи — запись 0 ($MFT)
/// содержит таблицу в $DATA. Прямой проход по цепочке атрибутов с проверкой
/// границ, как в <see cref="MftParser.ParseRecord"/>.
/// </summary>
public class MftDataRunsTests
{
    private static byte[] BuildFixupedRecord(MftRecordBuilder builder)
    {
        byte[] record = builder.Build();
        Assert.Null(MftParser.ApplyFixups(record, builder.BytesPerSector));
        return record;
    }

    [Fact]
    public void TryLocate_NonResidentData_ReturnsRunSpan()
    {
        byte[] runs = [0x11, 0x10, 0x2A, 0x00];
        var builder = new MftRecordBuilder(recordNumber: 0);
        builder.AddNonResidentData(16 * 4096UL, 16 * 4096UL, runs);
        byte[] record = BuildFixupedRecord(builder);

        bool ok = MftDataRuns.TryLocate(record, out ReadOnlySpan<byte> runList);

        Assert.True(ok);
        // спан накрывает раны и паддинг до границы атрибута (0x40 + 4 → 0x48)
        Assert.Equal(8, runList.Length);
        Assert.True(runList[..runs.Length].SequenceEqual(runs));
    }

    [Fact]
    public void TryLocate_ResidentDataOnly_ReturnsFalse()
    {
        var builder = new MftRecordBuilder(recordNumber: 0);
        builder.AddResidentData([1, 2, 3]);
        byte[] record = BuildFixupedRecord(builder);

        Assert.False(MftDataRuns.TryLocate(record, out ReadOnlySpan<byte> runList));
        Assert.Equal(0, runList.Length);
    }

    [Fact]
    public void TryLocate_NamedResidentIgnored_UnnamedNonResidentFound()
    {
        byte[] runs = [0x11, 0x08, 0x05, 0x00];
        var builder = new MftRecordBuilder(recordNumber: 0);
        builder.AddNamedData("zone.identifier", [0xAB]);
        builder.AddNonResidentData(8 * 4096UL, 8 * 4096UL, runs);
        byte[] record = BuildFixupedRecord(builder);

        bool ok = MftDataRuns.TryLocate(record, out ReadOnlySpan<byte> runList);

        Assert.True(ok);
        Assert.True(runList[..runs.Length].SequenceEqual(runs));
    }

    [Fact]
    public void TryLocate_NoDataAttribute_ReturnsFalse()
    {
        var builder = new MftRecordBuilder(recordNumber: 0);
        builder.AddFileName("$MFT", MftNamespace.Win32, 5, 7);
        byte[] record = BuildFixupedRecord(builder);

        Assert.False(MftDataRuns.TryLocate(record, out _));
    }

    [Fact]
    public void TryLocate_NotARecord_ReturnsFalse()
    {
        Assert.False(MftDataRuns.TryLocate(new byte[1024], out _));
    }

    [Fact]
    public void TryLocate_MalformedAttributeLength_ReturnsFalse()
    {
        var builder = new MftRecordBuilder(recordNumber: 0);
        builder.AddNonResidentData(16 * 4096UL, 16 * 4096UL, [0x11, 0x10, 0x2A, 0x00]);
        byte[] record = BuildFixupedRecord(builder);

        // $DATA — первый атрибут на 0x38 (после заголовка и USA); портим его длину
        MftTestBytes.PutU32(record, 0x38 + 0x04, 0x1000);

        Assert.False(MftDataRuns.TryLocate(record, out _));
    }
}
