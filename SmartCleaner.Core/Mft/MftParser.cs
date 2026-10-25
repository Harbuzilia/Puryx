using System.Buffers.Binary;
using System.Text;

namespace SmartCleaner.Core.Mft;

/// <summary>
/// Pure-функции разбора MFT-записей NTFS (День 7, ROADMAP v3.1): без доступа к тому,
/// том и дерево — День 8. Все данные считаются ненадёжными: границы проверяются
/// на каждом шаге, структурная порча даёт <see cref="MftRecordError.Malformed"/>,
/// а не исключение. Контракт вызова (День 8): сектор прочитан →
/// <see cref="ApplyFixups"/> (восстановление USA) → <see cref="ParseRecord"/>.
/// Заголовок в раскладке XP+ (self-референс записи на 0x2C); записи NT4-эпохи
/// не разбираются — томы XP+ покрывают всю практику.
/// </summary>
public static class MftParser
{
    private const int MinRecordSize = 0x30;        // заголовок FILE XP+
    private const int ResidentHeaderSize = 0x18;   // минимальный резидентный атрибут
    private const int FileNameValuePrefix = 0x42;  // parentRef(8)+times(32)+sizes(16)+flags/EA(8)+len+ns(2)
    private const int NonResidentMinSize = 0x38;   // нерезидентный заголовок до real size включительно
    private const int MinSectorSize = 512;         // меньшего сектора на NTFS не бывает

    private const uint MagicFile = 0x454C4946;      // "FILE" (LE)
    private const uint MagicBaad = 0x44414142;      // "BAAD" (LE)
    private const uint AttributeEnd = 0xFFFFFFFF;
    private const uint AttributeTypeList = 0x20;
    private const uint AttributeTypeFileName = 0x30;
    private const uint AttributeTypeData = 0x80;
    private const ulong RecordNumberMask = 0x0000FFFFFFFFFFFF;

    /// <summary>
    /// Восстанавливает Update Sequence Array записи IN PLACE: хвост каждого сектора
    /// (2 байта) содержит тег USA, оригиналы сохранены в массиве по смещению из заголовка.
    /// При несовпадении хвоста с тегом возвращает <see cref="MftRecordError.TornRead"/>
    /// (запись изменилась во время чтения). Страйд — сектор тома (0x1FE/0x3FE для 1024/512);
    /// если запись не кратна сектору (4Kn-том, запись меньше сектора), страйд выводится
    /// из USA-каунта. Возвращает null при успехе.
    /// </summary>
    /// <param name="record">Байты записи; замещается восстановленными данными.</param>
    /// <param name="bytesPerSector">Байт на сектор тома из загрузочного сектора (512 или 4096).</param>
    public static MftRecordError? ApplyFixups(Span<byte> record, int bytesPerSector)
    {
        if (record.Length < 4)
            return MftRecordError.NotARecord;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(record[..4]);
        if (magic == MagicBaad)
            return MftRecordError.Corrupt;
        if (magic != MagicFile)
            return MftRecordError.NotARecord;
        if (record.Length < MinRecordSize)
            return MftRecordError.Malformed;

        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x04, 2));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 2));

        // USA обязана существовать (хотя бы один сектор) и лежать внутри записи после заголовка
        if (usaCount < 2)
            return MftRecordError.Malformed;
        if (usaOffset < MinRecordSize || usaOffset + 2 * usaCount > record.Length)
            return MftRecordError.Malformed;

        // Число фиксап-слотов: по секторам тома, а при некратной записи — вывод из USA-каунта
        int sectorCount;
        if (bytesPerSector > 0 && record.Length >= bytesPerSector && record.Length % bytesPerSector == 0)
        {
            sectorCount = record.Length / bytesPerSector;
            if (usaCount != sectorCount + 1)
                return MftRecordError.Malformed;
        }
        else
        {
            sectorCount = usaCount - 1;
            if (record.Length % sectorCount != 0)
                return MftRecordError.Malformed;
        }

        int stride = record.Length / sectorCount;
        if (stride < MinSectorSize)
            return MftRecordError.Malformed; // «сектор» меньше реального — данные порчены

        ushort tag = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset, 2));
        for (int i = 0; i < sectorCount; i++)
        {
            int tail = i * stride + stride - 2;
            ushort current = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(tail, 2));
            if (current != tag)
                return MftRecordError.TornRead;
            ushort original = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(usaOffset + 2 + 2 * i, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(record.Slice(tail, 2), original);
        }

        return null;
    }

    /// <summary>
    /// Разбирает (уже зафиксапленную) MFT-запись: сигнатура FILE/BAAD/zero, инварианты
    /// заголовка и USA, цепочка атрибутов ($FILE_NAME неймспейсы/parent-reference,
    /// неименованный $DATA resident/nonresident, $ATTRIBUTE_LIST, END) — с проверкой
    /// границ на каждом шаге. Никогда не бросает: ошибка — в <see cref="MftRecord.Error"/>.
    /// </summary>
    /// <param name="record">Байты записи после <see cref="ApplyFixups"/>.</param>
    /// <param name="recordSize">Ожидаемый размер записи MFT из загрузочного сектора.</param>
    public static MftRecord ParseRecord(ReadOnlySpan<byte> record, int recordSize)
    {
        if (record.Length < 4)
            return MftRecord.Invalid(MftRecordError.NotARecord);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(record[..4]);
        if (magic == MagicBaad)
            return MftRecord.Invalid(MftRecordError.Corrupt);
        if (magic != MagicFile)
            return MftRecord.Invalid(MftRecordError.NotARecord);
        if (record.Length < MinRecordSize || recordSize < MinRecordSize || record.Length < recordSize)
            return MftRecord.Invalid(MftRecordError.Malformed);

        int usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x04, 2));
        int usaCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 2));
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x10, 2));
        ushort linkCount = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x12, 2));
        int firstAttributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x14, 2));
        MftRecordFlags flags = (MftRecordFlags)(BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x16, 2)) & 0x0003);
        uint usedSize = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x18, 4));
        uint allocatedSize = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x1C, 4));
        ulong baseRecordReference = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(0x20, 8));
        uint recordNumber = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(0x2C, 4));

        // Инварианты заголовка (данные ненадёжные): USA внутри зоны заголовка,
        // атрибуты и usedSize — в пределах записи, allocated совпадает с размером
        if (usaCount < 2)
            return MftRecord.Invalid(MftRecordError.Malformed);
        if (usaOffset < MinRecordSize || usaOffset + 2 * usaCount > firstAttributeOffset)
            return MftRecord.Invalid(MftRecordError.Malformed);
        if (firstAttributeOffset > usedSize || usedSize > (uint)recordSize)
            return MftRecord.Invalid(MftRecordError.Malformed);
        if (allocatedSize != (uint)recordSize)
            return MftRecord.Invalid(MftRecordError.Malformed);

        string? bestFileName = null;
        ulong? parentFromBest = null;
        ushort? parentSequenceFromBest = null;
        ulong? parentFromDos = null;
        ushort? parentSequenceFromDos = null;
        ulong allocatedSizeData = 0;
        ulong realSizeData = 0;
        bool hasAttributeList = false;
        bool sawUnnamedData = false;

        int limit = (int)usedSize;
        int offset = firstAttributeOffset;
        while (offset + 8 <= limit)
        {
            uint attributeType = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset, 4));
            if (attributeType == AttributeEnd)
                break;

            uint attributeLength = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(offset + 4, 4));
            if (attributeLength < ResidentHeaderSize || (attributeLength & 7) != 0)
                return MftRecord.Invalid(MftRecordError.Malformed);
            if (attributeLength > (uint)(limit - offset))
                return MftRecord.Invalid(MftRecordError.Malformed);

            int attributeEnd = offset + (int)attributeLength;
            switch (attributeType)
            {
                case AttributeTypeFileName:
                    if (!TryParseFileName(
                            record, offset, attributeEnd,
                            ref bestFileName, ref parentFromBest, ref parentSequenceFromBest,
                            ref parentFromDos, ref parentSequenceFromDos))
                        return MftRecord.Invalid(MftRecordError.Malformed);
                    break;

                case AttributeTypeData:
                    if (!TryParseData(
                            record, offset, attributeEnd,
                            ref allocatedSizeData, ref realSizeData, ref sawUnnamedData))
                        return MftRecord.Invalid(MftRecordError.Malformed);
                    break;

                case AttributeTypeList:
                    hasAttributeList = true; // атрибуты (часть $DATA) могут быть в extension-записях
                    break;
            }

            offset = attributeEnd;
        }

        // DOS-only запись: имя отброшено, но родителя берём из DOS-имени
        if (bestFileName == null && parentFromDos != null)
        {
            parentFromBest = parentFromDos;
            parentSequenceFromBest = parentSequenceFromDos;
        }

        ulong? baseRecordNumber = baseRecordReference == 0
            ? null
            : baseRecordReference & RecordNumberMask;
        int extraLinks = linkCount > 0 ? linkCount - 1 : 0;
        MftRecordError? error = (flags & MftRecordFlags.InUse) == 0
            ? MftRecordError.Deleted
            : null;

        return new MftRecord(
            recordNumber,
            baseRecordNumber,
            bestFileName,
            sequence,
            parentFromBest,
            parentSequenceFromBest,
            allocatedSizeData,
            realSizeData,
            flags,
            extraLinks,
            hasAttributeList,
            error);
    }

    /// <summary>
    /// Разбирает резидентный $FILE_NAME: ссылка на родителя (6 байт номер + 2 sequence),
    /// длина имени (0x40), неймспейс (0x41), имя UTF-16 (0x42). Лучшее имя — первый
    /// не-DOS (POSIX=0/Win32=1/Win32AndDos=3); из DOS-имени (2) запоминается только родитель.
    /// </summary>
    private static bool TryParseFileName(
        ReadOnlySpan<byte> record,
        int attributeStart,
        int attributeEnd,
        ref string? bestFileName,
        ref ulong? parentFromBest,
        ref ushort? parentSequenceFromBest,
        ref ulong? parentFromDos,
        ref ushort? parentSequenceFromDos)
    {
        int attributeLength = attributeEnd - attributeStart;

        // $FILE_NAME всегда резидентный и без собственного имени атрибута
        if (record[attributeStart + 0x08] != 0 || record[attributeStart + 0x09] != 0)
            return false;

        uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attributeStart + 0x10, 4));
        int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(attributeStart + 0x14, 2));
        if (valueOffset < ResidentHeaderSize || valueOffset > attributeLength)
            return false;
        if (valueLength > (uint)(attributeLength - valueOffset))
            return false;

        int value = attributeStart + valueOffset;
        int nameBytes = record[value + 0x40] * 2;
        if (valueLength < (uint)(FileNameValuePrefix + nameBytes))
            return false;

        byte nameSpace = record[value + 0x41];
        ulong parentReference = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(value, 8));
        if (nameBytes == 0 || nameSpace > 3)
            return true; // вырожденное/чужое значение неймспейса — пропускаем имя, запись валидна

        if (nameSpace != 2)
        {
            if (bestFileName == null)
            {
                bestFileName = Encoding.Unicode.GetString(record.Slice(value + 0x42, nameBytes));
                parentFromBest = parentReference & RecordNumberMask;
                parentSequenceFromBest = (ushort)(parentReference >> 48);
            }
        }
        else if (parentFromDos == null)
        {
            parentFromDos = parentReference & RecordNumberMask;
            parentSequenceFromDos = (ushort)(parentReference >> 48);
        }

        return true;
    }

    /// <summary>
    /// Разбирает $DATA: учитывается только НЕИМЕНОВАННЫЙ поток (именованные ADS —
    /// zone.identifier и т.п. — не содержимое файла). Резидентный: RealSize = длина
    /// значения, AllocatedSize = 0. Нерезидентный: размеры из полей 0x28/0x30.
    /// </summary>
    private static bool TryParseData(
        ReadOnlySpan<byte> record,
        int attributeStart,
        int attributeEnd,
        ref ulong allocatedSize,
        ref ulong realSize,
        ref bool sawUnnamedData)
    {
        int attributeLength = attributeEnd - attributeStart;

        byte resident = record[attributeStart + 0x08];
        int streamNameLength = record[attributeStart + 0x09];
        if (streamNameLength > 0)
        {
            // Именованный поток: проверяем, что имя помещается в атрибут, и игнорируем
            int streamNameOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(attributeStart + 0x0A, 2));
            if (streamNameOffset < ResidentHeaderSize || 2 * streamNameLength > attributeLength - streamNameOffset)
                return false;
            return true;
        }

        if (sawUnnamedData)
            return true; // второй неименованный $DATA невозможен в валидной записи; первый каноничен

        if (resident == 0)
        {
            uint valueLength = BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(attributeStart + 0x10, 4));
            int valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(attributeStart + 0x14, 2));
            if (valueOffset < ResidentHeaderSize || valueOffset > attributeLength)
                return false;
            if (valueLength > (uint)(attributeLength - valueOffset))
                return false;
            realSize = valueLength;
            allocatedSize = 0;
        }
        else if (resident == 1)
        {
            if (attributeLength < NonResidentMinSize)
                return false;
            allocatedSize = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(attributeStart + 0x28, 8));
            realSize = BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(attributeStart + 0x30, 8));
        }
        else
        {
            return false;
        }

        sawUnnamedData = true;
        return true;
    }
}
