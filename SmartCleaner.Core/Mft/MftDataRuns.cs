using System.Buffers.Binary;

namespace SmartCleaner.Core.Mft;

/// <summary>
/// Pure-хелпер (День 8): поиск run-list неименованного нерезидентного $DATA
/// в (уже зафиксапленной) MFT-записи — запись 0 ($MFT) содержит таблицу
/// файлов в $DATA. Прямой проход по цепочке атрибутов с проверкой границ,
/// как в <see cref="MftParser.ParseRecord"/>; порча → false.
/// </summary>
public static class MftDataRuns
{
    private const int MinRecordSize = 0x30;
    private const int MinAttributeSize = 0x18;
    private const int MinNonResidentHeader = 0x40; // раны начинаются не раньше конца заголовка
    private const uint MagicFile = 0x454C4946;     // "FILE" (LE)
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const uint AttributeTypeData = 0x80;

    /// <summary>
    /// Ищет первый неименованный нерезидентный $DATA и возвращает спан его
    /// ранов: [начало атрибута + смещение ранов (поле 0x20), конец атрибута).
    /// Терминатор 0x00 и паддинг до 8-байтной границы входят в спан —
    /// декодер останавливается по терминатору. Именованные (ADS) и
    /// резидентные потоки пропускаются.
    /// </summary>
    /// <param name="record">Байты записи после <see cref="MftParser.ApplyFixups"/>.</param>
    /// <param name="runList">Спан ранов; пустой при false.</param>
    public static bool TryLocate(ReadOnlySpan<byte> record, out ReadOnlySpan<byte> runList)
    {
        runList = default;

        if (record.Length < MinRecordSize)
            return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(record[..4]) != MagicFile)
            return false;

        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x04, 2));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 2));
        int firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x14, 2));
        uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x18, 4));

        if (usaCount < 2)
            return false;
        if (usaOffset < MinRecordSize || usaOffset + 2 * usaCount > firstAttributeOffset)
            return false;
        if (firstAttributeOffset > usedSize || usedSize > (uint)record.Length)
            return false;

        int limit = (int)usedSize;
        int offset = firstAttributeOffset;
        while (offset + 8 <= limit)
        {
            uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset, 4));
            if (attributeType == AttributeEnd)
                break;

            uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset + 4, 4));
            if (attributeLength < MinAttributeSize || (attributeLength & 7) != 0)
                return false;
            if (attributeLength > (uint)(limit - offset))
                return false;

            if (attributeType == AttributeTypeData
                && record[offset + 0x08] == 1  // нерезидентный
                && record[offset + 0x09] == 0) // неименованный
            {
                if (attributeLength < MinNonResidentHeader)
                    return false;
                int runOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(offset + 0x20, 2));
                if (runOffset < MinNonResidentHeader || runOffset > (int)attributeLength)
                    return false;

                runList = record.Slice(offset + runOffset, (int)attributeLength - runOffset);
                return true;
            }

            offset += (int)attributeLength;
        }

        return false;
    }
}
