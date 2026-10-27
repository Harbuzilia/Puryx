using System.Buffers.Binary;

namespace SmartCleaner.Core.Mft;

/// <summary>
/// Загрузочный сектор NTFS (OEM + BPB): геометрия тома и расположение $MFT.
/// Pure-данные; разбор — <see cref="TryParse"/>, поля считаются ненадёжными
/// и валидируются консервативно: ошибка → false, без исключений.
/// Отклонения от текста задачи (фаза M, проверено эмпирически на живом томе
/// + FSCTL_GET_NTFS_VOLUME_DATA + Wikipedia «Partition Boot Sector»):
/// 0x24 — EBPB (не используется NTFS), TotalSectors — 0x28, $MFT Lcn — 0x30,
/// hint записи — 0x40.
/// </summary>
public readonly struct NtfsBootSector
{
    private const int BootSectorSize = 512;
    private const int OemOffset = 0x03;
    private const int BytesPerSectorOffset = 0x0B;
    private const int SectorsPerClusterOffset = 0x0D;
    private const int TotalSectorsOffset = 0x28;
    private const int MftLcnOffset = 0x30;
    private const int RecordSizeHintOffset = 0x40;
    private const int BootSignatureOffset = 0x1FE;

    private const ushort BootSignature = 0xAA55;
    private const int MinBytesPerSector = 512;
    private const int MaxBytesPerSector = 4096;
    private const int MaxSectorsPerCluster = 128;
    private const int MaxHintExponent = 30;   // 2^30 — потолок для отрицательного 0x40
    private const int MinRecordSize = 256;
    private const int MaxRecordSize = 1 << 20;

    private NtfsBootSector(
        int bytesPerSector,
        int bytesPerCluster,
        long mftStartLcn,
        long totalSectors,
        int? fileRecordSize)
    {
        BytesPerSector = bytesPerSector;
        BytesPerCluster = bytesPerCluster;
        MftStartLcn = mftStartLcn;
        TotalSectors = totalSectors;
        FileRecordSize = fileRecordSize;
    }

    /// <summary>Байт на сектор (512–4096, степень двойки; поле 0x0B).</summary>
    public int BytesPerSector { get; }

    /// <summary>Байт на кластер (сектор × кластеры; поля 0x0B/0x0D).</summary>
    public int BytesPerCluster { get; }

    /// <summary>Кластер начала $MFT (поле 0x30, знаковый; &gt; 0 и внутри тома).</summary>
    public long MftStartLcn { get; }

    /// <summary>Всего секторов на томе (поле 0x28, знаковый).</summary>
    public long TotalSectors { get; }

    /// <summary>
    /// Размер MFT-записи из поля 0x40 (знаковый байт): отрицательное v → 2^-v байт,
    /// положительное → v × кластер; null = не задан (0) — потребитель применяет
    /// эвристику (4096 при секторе 4096, иначе 1024).
    /// </summary>
    public int? FileRecordSize { get; }

    /// <summary>
    /// Разбирает загрузочный сектор NTFS: OEM «NTFS    », сигнатура 0x55AA,
    /// степени двойки для сектора/кластера, $MFT внутри тома. Повреждённые
    /// или не-NTFS поля → false (out-значение default).
    /// </summary>
    /// <param name="sector">Байты загрузочного сектора (минимум 512).</param>
    /// <param name="boot">Разобранная геометрия при успехе.</param>
    public static bool TryParse(ReadOnlySpan<byte> sector, out NtfsBootSector boot)
    {
        boot = default;
        if (sector.Length < BootSectorSize)
            return false;

        if (!sector.Slice(OemOffset, 8).SequenceEqual("NTFS    "u8))
            return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(sector.Slice(BootSignatureOffset, 2)) != BootSignature)
            return false;

        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(sector.Slice(BytesPerSectorOffset, 2));
        if (!IsPowerOfTwo(bytesPerSector)
            || bytesPerSector < MinBytesPerSector
            || bytesPerSector > MaxBytesPerSector)
            return false;

        int sectorsPerCluster = sector[SectorsPerClusterOffset];
        if (!IsPowerOfTwo(sectorsPerCluster) || sectorsPerCluster > MaxSectorsPerCluster)
            return false;

        long totalSectors = BinaryPrimitives.ReadInt64LittleEndian(sector.Slice(TotalSectorsOffset, 8));
        long mftLcn = BinaryPrimitives.ReadInt64LittleEndian(sector.Slice(MftLcnOffset, 8));
        if (totalSectors <= 0 || mftLcn <= 0 || mftLcn >= totalSectors)
            return false;

        sbyte hint = (sbyte)sector[RecordSizeHintOffset];
        if (hint < -MaxHintExponent)
            return false; // 2^-v не помещается в int — порча

        int? recordSize = hint switch
        {
            0 => null,                                  // не задан
            < 0 => 1 << -hint,                          // 2^(-v) байт
            _ => hint * (bytesPerSector * sectorsPerCluster), // v × кластер
        };
        if (recordSize is int size && (size < MinRecordSize || size > MaxRecordSize || !IsPowerOfTwo(size)))
            return false;

        boot = new NtfsBootSector(
            bytesPerSector,
            bytesPerSector * sectorsPerCluster,
            mftLcn,
            totalSectors,
            recordSize);
        return true;
    }

    private static bool IsPowerOfTwo(int value)
        => value > 0 && (value & (value - 1)) == 0;
}
