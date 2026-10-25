using System.Text;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Неймспейсы $FILE_NAME (байт namespace в значении атрибута).
/// </summary>
internal enum MftNamespace : byte
{
    Posix = 0,
    Win32 = 1,
    Dos = 2,
    Win32AndDos = 3,
}

/// <summary>
/// Хелпер сборки СИНТЕТИЧЕСКИХ MFT-записей для тестов парсера (День 7, ROADMAP v3.1).
/// Строит запись в «дисковой» форме: хвосты секторов содержат тег USA,
/// оригинальные байты сохранены в Update Sequence Array — тест обязан
/// вызвать <see cref="SmartCleaner.Core.Mft.MftParser.ApplyFixups"/> перед
/// <see cref="SmartCleaner.Core.Mft.MftParser.ParseRecord"/> (поток Дня 8).
/// </summary>
internal sealed class MftRecordBuilder
{
    private readonly uint _recordNumber;
    private readonly ushort _sequence;
    private readonly ushort _usaTag;
    private readonly List<byte[]> _attributes = [];

    /// <summary>Создаёт билдер записи с заданными полями заголовка.</summary>
    public MftRecordBuilder(
        uint recordNumber = 42,
        ushort sequence = 7,
        ushort usaTag = 0x1234,
        int recordSize = 1024,
        int bytesPerSector = 512)
    {
        if (recordSize % bytesPerSector != 0)
            throw new NotSupportedException("Fixture requires recordSize to be a multiple of bytesPerSector");
        RecordSize = recordSize;
        BytesPerSector = bytesPerSector;
        _recordNumber = recordNumber;
        _sequence = sequence;
        _usaTag = usaTag;
    }

    public int RecordSize { get; }
    public int BytesPerSector { get; }

    /// <summary>Флаги записи (InUse/Directory).</summary>
    public MftRecordFlags Flags { get; set; } = MftRecordFlags.InUse;

    /// <summary>Счётчик ссылок (жёстких связей) из заголовка.</summary>
    public ushort LinkCount { get; set; } = 1;

    /// <summary>Базовая запись для extension-записи; null = базовая.</summary>
    public ulong? BaseRecordNumber { get; set; }

    /// <summary>Sequence базовой записи (старшие 16 бит ссылки).</summary>
    public ushort BaseSequence { get; set; } = 9;

    /// <summary>Добавляет $FILE_NAME (0x30) с заданным неймспейсом и ссылкой на родителя.</summary>
    public void AddFileName(string name, MftNamespace ns, ulong parentNumber, ushort parentSequence)
    {
        if (name.Length > 255)
            throw new NotSupportedException("MFT filename is limited to 255 characters");

        int valueLength = 0x42 + checked(name.Length * 2);
        int total = (0x18 + valueLength + 7) & ~7;
        var a = new byte[total];

        MftTestBytes.PutU32(a, 0x00, 0x30);
        MftTestBytes.PutU32(a, 0x04, (uint)total);
        a[0x08] = 0; // резидентный
        a[0x09] = 0; // атрибут без собственного имени
        MftTestBytes.PutU32(a, 0x10, (uint)valueLength);
        MftTestBytes.PutU16(a, 0x14, 0x18);

        int v = 0x18;
        MftTestBytes.PutU64(a, v + 0x00, parentNumber | ((ulong)parentSequence << 48));
        // 4 таймстампа (0x08..0x27), allocated (0x28), real (0x30), flags (0x38), EA (0x3C) — нули
        a[v + 0x40] = (byte)name.Length;
        a[v + 0x41] = (byte)ns;
        Encoding.Unicode.GetBytes(name).CopyTo(a, v + 0x42);

        _attributes.Add(a);
    }

    /// <summary>Добавляет неименованный резидентный $DATA (0x80) с полезной нагрузкой.</summary>
    public void AddResidentData(byte[] payload)
    {
        int total = (0x18 + payload.Length + 7) & ~7;
        var a = new byte[total];

        MftTestBytes.PutU32(a, 0x00, 0x80);
        MftTestBytes.PutU32(a, 0x04, (uint)total);
        a[0x08] = 0; // резидентный
        a[0x09] = 0; // неименованный
        MftTestBytes.PutU32(a, 0x10, (uint)payload.Length);
        MftTestBytes.PutU16(a, 0x14, 0x18);
        payload.CopyTo(a, 0x18);

        _attributes.Add(a);
    }

    /// <summary>Добавляет неименованный нерезидентный $DATA (0x80) с размерами из заголовка.</summary>
    public void AddNonResidentData(ulong allocatedSize, ulong realSize)
    {
        const int total = 0x48; // нерезидентный заголовок 0x40 + терминатор ранов с выравниванием
        var a = new byte[total];

        MftTestBytes.PutU32(a, 0x00, 0x80);
        MftTestBytes.PutU32(a, 0x04, total);
        a[0x08] = 1; // нерезидентный
        a[0x09] = 0; // неименованный
        MftTestBytes.PutU64(a, 0x10, 0);                                    // стартовый VCN
        MftTestBytes.PutU64(a, 0x18, allocatedSize / 4096 == 0 ? 0 : allocatedSize / 4096 - 1); // последний VCN
        MftTestBytes.PutU16(a, 0x20, 0x40);                                 // смещение ранов
        MftTestBytes.PutU64(a, 0x28, allocatedSize);
        MftTestBytes.PutU64(a, 0x30, realSize);
        MftTestBytes.PutU64(a, 0x38, realSize);                             // инициализированный размер
        a[0x40] = 0;                                                        // пустой список ранов

        _attributes.Add(a);
    }

    /// <summary>Добавляет ИМЕНОВАННЫЙ поток $DATA (ADS, например zone.identifier) — парсер обязан его игнорировать.</summary>
    public void AddNamedData(string streamName, byte[] payload)
    {
        if (streamName.Length > 255)
            throw new NotSupportedException("MFT stream name is limited to 255 characters");

        int nameBytes = checked(streamName.Length * 2);
        int valueOffset = (0x18 + nameBytes + 7) & ~7;
        int total = (valueOffset + payload.Length + 7) & ~7;
        var a = new byte[total];

        MftTestBytes.PutU32(a, 0x00, 0x80);
        MftTestBytes.PutU32(a, 0x04, (uint)total);
        a[0x08] = 0; // резидентный
        a[0x09] = (byte)streamName.Length;
        MftTestBytes.PutU16(a, 0x0A, 0x18); // смещение имени атрибута
        MftTestBytes.PutU32(a, 0x10, (uint)payload.Length);
        MftTestBytes.PutU16(a, 0x14, (ushort)valueOffset);
        Encoding.Unicode.GetBytes(streamName).CopyTo(a, 0x18);
        payload.CopyTo(a, valueOffset);

        _attributes.Add(a);
    }

    /// <summary>Добавляет $ATTRIBUTE_LIST (0x20) — флаг для парсера.</summary>
    public void AddAttributeList()
    {
        const int total = 0x28; // заголовок 0x18 + 16 байт значения
        var a = new byte[total];

        MftTestBytes.PutU32(a, 0x00, 0x20);
        MftTestBytes.PutU32(a, 0x04, total);
        a[0x08] = 0;
        a[0x09] = 0;
        MftTestBytes.PutU32(a, 0x10, 16);
        MftTestBytes.PutU16(a, 0x14, 0x18);

        _attributes.Add(a);
    }

    /// <summary>Собирает запись в «дисковой» форме (хвосты секторов = тег USA).</summary>
    public byte[] Build()
    {
        var record = new byte[RecordSize];
        int sectorCount = RecordSize / BytesPerSector;
        int usaCount = sectorCount + 1;
        const int usaOffset = 0x30;
        int firstAttr = (usaOffset + 2 * usaCount + 7) & ~7;

        // Заголовок FILE (XP+: self-референс на 0x2C)
        Encoding.ASCII.GetBytes("FILE").CopyTo(record, 0x00);
        MftTestBytes.PutU16(record, 0x04, (ushort)usaOffset);
        MftTestBytes.PutU16(record, 0x06, (ushort)usaCount);
        MftTestBytes.PutU64(record, 0x08, 0);                      // $LogFile LSN
        MftTestBytes.PutU16(record, 0x10, _sequence);
        MftTestBytes.PutU16(record, 0x12, LinkCount);
        MftTestBytes.PutU16(record, 0x14, (ushort)firstAttr);
        MftTestBytes.PutU16(record, 0x16, (ushort)Flags);
        MftTestBytes.PutU32(record, 0x1C, (uint)RecordSize);       // выделенный размер записи
        MftTestBytes.PutU64(record, 0x20, BaseRecordNumber.HasValue
            ? BaseRecordNumber.Value | ((ulong)BaseSequence << 48)
            : 0UL);
        MftTestBytes.PutU16(record, 0x28, (ushort)(_attributes.Count + 1)); // next attribute id
        MftTestBytes.PutU32(record, 0x2C, _recordNumber);

        // Атрибуты + терминатор END
        int offset = firstAttr;
        foreach (byte[] attribute in _attributes)
        {
            attribute.CopyTo(record, offset);
            offset += attribute.Length;
        }
        MftTestBytes.PutU32(record, offset, 0xFFFFFFFF);
        MftTestBytes.PutU32(record, offset + 4, 0);
        offset += 8;

        int usedSize = (offset + 7) & ~7;
        if (usedSize > RecordSize)
            throw new InvalidOperationException("Fixture attributes do not fit into the record");
        MftTestBytes.PutU32(record, 0x18, (uint)usedSize);

        // Дисковая форма USA: тег в хвостах, оригиналы — в массиве
        MftTestBytes.PutU16(record, usaOffset, _usaTag);
        for (int i = 0; i < sectorCount; i++)
        {
            int tail = (i + 1) * BytesPerSector - 2;
            ushort original = MftTestBytes.GetU16(record, tail);
            MftTestBytes.PutU16(record, usaOffset + 2 + 2 * i, original);
            MftTestBytes.PutU16(record, tail, _usaTag);
        }

        return record;
    }
}

/// <summary>
/// Мини-хелперы ручной сборки байтов (little-endian) для тестов без билдера.
/// </summary>
internal static class MftTestBytes
{
    public static void PutU16(byte[] b, int offset, ushort value)
    {
        b[offset] = (byte)value;
        b[offset + 1] = (byte)(value >> 8);
    }

    public static void PutU32(byte[] b, int offset, uint value)
    {
        b[offset] = (byte)value;
        b[offset + 1] = (byte)(value >> 8);
        b[offset + 2] = (byte)(value >> 16);
        b[offset + 3] = (byte)(value >> 24);
    }

    public static void PutU64(byte[] b, int offset, ulong value)
    {
        PutU32(b, offset, (uint)value);
        PutU32(b, offset + 4, (uint)(value >> 32));
    }

    public static ushort GetU16(byte[] b, int offset)
        => (ushort)(b[offset] | (b[offset + 1] << 8));

    public static void WriteMagic(byte[] b, string magic)
        => Encoding.ASCII.GetBytes(magic).CopyTo(b, 0);
}
