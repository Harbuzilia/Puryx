using System.Text;
using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 8 (ROADMAP v3.1, фаза M): контракты разбора загрузочного сектора NTFS
/// на синтетических фикстурах — геометрия тома и расположение $MFT (макет
/// ntfsdoc: байт/сектор 0x0B, кластеры 0x0D, всего секторов 0x24, $MFT Lcn
/// 0x2C — НЕ 0x24, кластеры на запись 0x3C — знаковый байт).
/// </summary>
public class NtfsBootSectorTests
{
    /// <summary>Собирает загрузочный сектор 512 байт с заданной геометрией.</summary>
    private static byte[] Boot(
        int bytesPerSector = 512,
        int sectorsPerCluster = 8,
        long totalSectors = 0x0000_1234_5678,
        long mftLcn = 0x000C_0000,
        sbyte fileRecordHint = -10)
    {
        var sector = new byte[512];
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(sector, 0x03);
        MftTestBytes.PutU16(sector, 0x0B, (ushort)bytesPerSector);
        sector[0x0D] = (byte)sectorsPerCluster;
        MftTestBytes.PutU64(sector, 0x24, (ulong)totalSectors);
        MftTestBytes.PutU64(sector, 0x2C, (ulong)mftLcn);
        sector[0x3C] = (byte)fileRecordHint;
        MftTestBytes.PutU16(sector, 0x1FE, 0xAA55); // сигнатура загрузочного сектора
        return sector;
    }

    [Fact]
    public void TryParse_ValidBoot_ParsesGeometry()
    {
        byte[] sector = Boot();

        bool ok = NtfsBootSector.TryParse(sector, out NtfsBootSector boot);

        Assert.True(ok);
        Assert.Equal(512, boot.BytesPerSector);
        Assert.Equal(4096, boot.BytesPerCluster);
        Assert.Equal(0x000C_0000, boot.MftStartLcn);
        Assert.Equal(0x0000_1234_5678, boot.TotalSectors);
        Assert.Equal(1024, boot.FileRecordSize);
    }

    [Fact]
    public void TryParse_4KnSector_SingleSectorCluster()
    {
        byte[] sector = Boot(bytesPerSector: 4096, sectorsPerCluster: 1, fileRecordHint: -12);

        bool ok = NtfsBootSector.TryParse(sector, out NtfsBootSector boot);

        Assert.True(ok);
        Assert.Equal(4096, boot.BytesPerSector);
        Assert.Equal(4096, boot.BytesPerCluster);
        Assert.Equal(4096, boot.FileRecordSize);
    }

    [Fact]
    public void TryParse_OemIdNotNtfs_ReturnsFalse()
    {
        byte[] sector = Boot();
        Encoding.ASCII.GetBytes("FAT32   ").CopyTo(sector, 0x03);

        Assert.False(NtfsBootSector.TryParse(sector, out _));
    }

    [Fact]
    public void TryParse_BootSignatureMissing_ReturnsFalse()
    {
        byte[] sector = Boot();
        MftTestBytes.PutU16(sector, 0x1FE, 0);

        Assert.False(NtfsBootSector.TryParse(sector, out _));
    }

    [Fact]
    public void TryParse_SectorSizeNotPowerOfTwo_ReturnsFalse()
    {
        Assert.False(NtfsBootSector.TryParse(Boot(bytesPerSector: 1000), out _));
    }

    [Fact]
    public void TryParse_SectorSizeOutOfRange_ReturnsFalse()
    {
        // (a) меньше минимального сектора NTFS, (b) больше допустимого
        Assert.False(NtfsBootSector.TryParse(Boot(bytesPerSector: 256), out _));
        Assert.False(NtfsBootSector.TryParse(Boot(bytesPerSector: 8192), out _));
    }

    [Fact]
    public void TryParse_SectorsPerClusterNotPowerOfTwo_ReturnsFalse()
    {
        Assert.False(NtfsBootSector.TryParse(Boot(sectorsPerCluster: 3), out _));
    }

    [Fact]
    public void TryParse_SectorsPerClusterTooLarge_ReturnsFalse()
    {
        Assert.False(NtfsBootSector.TryParse(Boot(sectorsPerCluster: 256), out _));
    }

    [Fact]
    public void TryParse_MftLcnZeroOrBeyondVolume_ReturnsFalse()
    {
        // (a) Lcn = 0, (b) Lcn за пределами тома (равен totalSectors)
        Assert.False(NtfsBootSector.TryParse(Boot(mftLcn: 0), out _));
        Assert.False(NtfsBootSector.TryParse(Boot(mftLcn: 0x0000_1234_5678), out _));
    }

    [Fact]
    public void TryParse_TotalSectorsZero_ReturnsFalse()
    {
        Assert.False(NtfsBootSector.TryParse(Boot(totalSectors: 0), out _));
    }

    [Fact]
    public void TryParse_FileRecordHint_Variants()
    {
        // (a) отрицательный: 2^(-v) байт; (b) положительный: v × кластер; (c) 0: не задан
        Assert.True(NtfsBootSector.TryParse(Boot(fileRecordHint: -9), out NtfsBootSector negative));
        Assert.Equal(512, negative.FileRecordSize);

        Assert.True(NtfsBootSector.TryParse(Boot(fileRecordHint: 2), out NtfsBootSector positive));
        Assert.Equal(2 * 4096, positive.FileRecordSize);

        Assert.True(NtfsBootSector.TryParse(Boot(fileRecordHint: 0), out NtfsBootSector unset));
        Assert.Null(unset.FileRecordSize);
    }

    [Fact]
    public void TryParse_TruncatedBuffer_ReturnsFalse()
    {
        Assert.False(NtfsBootSector.TryParse(Boot()[..511], out _));
    }
}
