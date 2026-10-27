using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 8 (ROADMAP v3.1, фаза M): контракты декодера run-list NTFS —
/// экстенты $DATA в формате «заголовок, ран за раном»: младший ниббл
/// заголовка = размер счётчика, старший = размер смещения; смещения
/// знаковые кумулятивные (two's complement LE), размер смещения 0 =
/// спарс-ран (Lcn = 0). Порча → false без исключений.
/// </summary>
public class RunListDecoderTests
{
    /// <summary>Кодирует один ран: заголовок, счётчик (LE), смещение (LE, two's complement).</summary>
    private static byte[] EncodeRun(long count, int countSize, long offset = 0, int offsetSize = 0)
    {
        var run = new List<byte> { (byte)((offsetSize << 4) | countSize) };
        for (int i = 0; i < countSize; i++)
            run.Add((byte)(count >> (8 * i)));
        for (int i = 0; i < offsetSize; i++)
            run.Add((byte)(offset >> (8 * i)));
        return [.. run];
    }

    /// <summary>Клеит раны и терминатор 0x00.</summary>
    private static byte[] EncodeRunList(params byte[][] runs)
    {
        var list = new List<byte>();
        foreach (byte[] run in runs)
            list.AddRange(run);
        list.Add(0);
        return [.. list];
    }

    private static IReadOnlyList<RunExtent> Decode(params byte[][] runs)
    {
        byte[] runList = EncodeRunList(runs);
        Assert.True(RunListDecoder.TryDecode(runList, out IReadOnlyList<RunExtent> extents));
        return extents;
    }

    [Fact]
    public void TryDecode_SingleRun_DecodesExtent()
    {
        IReadOnlyList<RunExtent> extents = Decode(EncodeRun(16, 1, 42, 1));

        RunExtent extent = Assert.Single(extents);
        Assert.Equal(0, extent.StartVcn);
        Assert.Equal(42, extent.StartLcn);
        Assert.Equal(16, extent.LengthClusters);
        Assert.False(extent.IsSparse);
    }

    [Fact]
    public void TryDecode_TwoRuns_OffsetAccumulates()
    {
        IReadOnlyList<RunExtent> extents = Decode(
            EncodeRun(16, 1, 42, 1),
            EncodeRun(5, 1, 8, 1));

        Assert.Equal(2, extents.Count);
        Assert.Equal(16, extents[1].StartVcn);
        Assert.Equal(50, extents[1].StartLcn); // 42 + 8 — кумулятив
        Assert.Equal(5, extents[1].LengthClusters);
    }

    [Fact]
    public void TryDecode_NegativeOffset_DecrementsLcn()
    {
        IReadOnlyList<RunExtent> extents = Decode(
            EncodeRun(8, 1, 48, 1),
            EncodeRun(4, 1, -16, 1));

        Assert.Equal(2, extents.Count);
        Assert.Equal(32, extents[1].StartLcn); // 48 − 16
    }

    [Fact]
    public void TryDecode_MultiByteCountAndOffset()
    {
        IReadOnlyList<RunExtent> extents = Decode(EncodeRun(256, 2, 0x123456, 4));

        RunExtent extent = Assert.Single(extents);
        Assert.Equal(256, extent.LengthClusters);
        Assert.Equal(0x123456, extent.StartLcn);
    }

    [Fact]
    public void TryDecode_SparseRun_LcnZeroIncluded()
    {
        // размер смещения 0 = спарс: экстент включается в список (Lcn = 0)
        // ради VCN-непрерывности; LCN-аккумулятор не двигается
        IReadOnlyList<RunExtent> extents = Decode(
            EncodeRun(16, 1),              // спарс: offsetSize = 0
            EncodeRun(5, 1, 2, 1));

        Assert.Equal(2, extents.Count);
        Assert.Equal(0, extents[0].StartLcn);
        Assert.True(extents[0].IsSparse);
        Assert.Equal(16, extents[0].LengthClusters);
        Assert.Equal(16, extents[1].StartVcn);
        Assert.Equal(2, extents[1].StartLcn);
    }

    [Fact]
    public void TryDecode_Terminator_StopsDecoding()
    {
        byte[] runList = [0x11, 0x10, 0x2A, 0x00, 0x11, 0x01, 0x01]; // после 0x00 — мусор

        Assert.True(RunListDecoder.TryDecode(runList, out IReadOnlyList<RunExtent> extents));
        RunExtent extent = Assert.Single(extents);
        Assert.Equal(42, extent.StartLcn);
    }

    [Fact]
    public void TryDecode_MissingTerminator_DecodesRuns()
    {
        byte[] runList = [0x11, 0x10, 0x2A]; // терминатора нет — допустимо

        Assert.True(RunListDecoder.TryDecode(runList, out IReadOnlyList<RunExtent> extents));
        RunExtent extent = Assert.Single(extents);
        Assert.Equal(16, extent.LengthClusters);
    }

    [Fact]
    public void TryDecode_EmptyList_NoExtents()
    {
        Assert.True(RunListDecoder.TryDecode([0x00], out IReadOnlyList<RunExtent> extents));
        Assert.Empty(extents);
    }

    [Fact]
    public void TryDecode_EmptyBuffer_NoExtents()
    {
        Assert.True(RunListDecoder.TryDecode([], out IReadOnlyList<RunExtent> extents));
        Assert.Empty(extents);
    }

    [Fact]
    public void TryDecode_ZeroCountSize_ReturnsFalse()
    {
        Assert.False(RunListDecoder.TryDecode([0x10, 0x2A], out IReadOnlyList<RunExtent> extents));
        Assert.Empty(extents);
    }

    [Fact]
    public void TryDecode_FieldSizeTooLarge_ReturnsFalse()
    {
        // (a) размер счётчика 9; (b) размер смещения 9
        Assert.False(RunListDecoder.TryDecode([0x19, 0x01], out _));
        Assert.False(RunListDecoder.TryDecode([0x91, 0x01, 0x01], out _));
    }

    [Fact]
    public void TryDecode_TruncatedRun_ReturnsFalse()
    {
        // заголовок требует счётчик 1 байт + смещение 2 байта, но смещения нет
        Assert.False(RunListDecoder.TryDecode([0x21, 0x10], out _));
    }

    [Fact]
    public void TryDecode_ZeroCount_ReturnsFalse()
    {
        Assert.False(RunListDecoder.TryDecode([0x11, 0x00, 0x2A, 0x00], out _));
    }

    [Fact]
    public void TryDecode_LcnUnderflow_ReturnsFalse()
    {
        // первый ран со смещением −1: LCN уходит в минус
        Assert.False(RunListDecoder.TryDecode([0x11, 0x10, 0xFF, 0x00], out _));
    }

    [Fact]
    public void TryDecode_LcnBeyondSignedRange_ReturnsFalse()
    {
        byte[] maxOffset = EncodeRun(1, 1, long.MaxValue, 8);
        byte[] plusOne = EncodeRun(1, 1, 1, 1);

        Assert.False(RunListDecoder.TryDecode(EncodeRunList(maxOffset, plusOne), out _));
    }
}
