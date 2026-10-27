namespace SmartCleaner.Core.Mft;

/// <summary>
/// Флаги состояния MFT-записи (поле Flags заголовка FILE, известные биты).
/// </summary>
[Flags]
public enum MftRecordFlags
{
    /// <summary>Запись используется (файл существует).</summary>
    None = 0,

    /// <summary>Запись используется.</summary>
    InUse = 0x0001,

    /// <summary>Запись описывает каталог.</summary>
    Directory = 0x0002,
}

/// <summary>
/// Классификация ошибки при чтении/разборе MFT-записи.
/// null в <see cref="MftRecord.Error"/> означает успешный разбор.
/// </summary>
public enum MftRecordError
{
    /// <summary>Слот пуст (магия не FILE: нули/мусор) — записи нет.</summary>
    NotARecord,

    /// <summary>Магия BAAD — запись помечена NTFS как повреждённая.</summary>
    Corrupt,

    /// <summary>USA-мисматч: хвост сектора не равен тегу — запись изменилась во время чтения.</summary>
    TornRead,

    /// <summary>Структурная порча: выходы за границы, невозможные поля заголовка/атрибутов.</summary>
    Malformed,

    /// <summary>Запись корректна, но файл удалён (флаг InUse сброшен).</summary>
    Deleted,
}

/// <summary>
/// Результат разбора одной MFT-записи NTFS (pure-данные, без доступа к тому).
/// Значения размеров — только из неименованного потока $DATA:
/// для резидентного AllocatedSize = 0 (кластеры не выделены), для каталога — 0/0.
/// Лучшее имя — первый не-DOS $FILE_NAME; если есть только DOS 8.3, имя = null,
/// но родитель берётся из DOS-имени (дерево Дня 8 строится по родителю).
/// </summary>
public readonly struct MftRecord
{
    internal MftRecord(
        ulong number,
        ulong? baseRecordNumber,
        string? bestFileName,
        ushort sequence,
        ulong? parentRecordNumber,
        ushort? parentSequence,
        ulong allocatedSize,
        ulong realSize,
        MftRecordFlags flags,
        int extraLinks,
        bool hasAttributeList,
        MftRecordError? error)
    {
        Number = number;
        BaseRecordNumber = baseRecordNumber;
        BestFileName = bestFileName;
        Sequence = sequence;
        ParentRecordNumber = parentRecordNumber;
        ParentSequence = parentSequence;
        AllocatedSize = allocatedSize;
        RealSize = realSize;
        Flags = flags;
        ExtraLinks = extraLinks;
        HasAttributeList = hasAttributeList;
        Error = error;
    }

    /// <summary>Запись с ошибкой без данных (частичные данные ненадёжного источника отбрасываются).</summary>
    internal static MftRecord Invalid(MftRecordError error)
        => new(0, null, null, 0, null, null, 0, 0, MftRecordFlags.None, 0, false, error);

    /// <summary>Копия записи с заменёнными размерами (слияние extension-записей, День 8).</summary>
    internal MftRecord WithSizes(ulong allocatedSize, ulong realSize)
        => new(Number, BaseRecordNumber, BestFileName, Sequence, ParentRecordNumber, ParentSequence,
            allocatedSize, realSize, Flags, ExtraLinks, HasAttributeList, Error);

    /// <summary>Номер записи (self-референс заголовка XP+; День 8 сверяет с позицией в $MFT).</summary>
    public ulong Number { get; }

    /// <summary>Номер базовой записи для extension-записи; null = базовая запись.</summary>
    public ulong? BaseRecordNumber { get; }

    /// <summary>Лучшее имя файла: первый $FILE_NAME не-DOS неймспейса; null, если имён нет или только DOS.</summary>
    public string? BestFileName { get; }

    /// <summary>Sequence-номер записи из заголовка (инкремент при реиспользовании слота).</summary>
    public ushort Sequence { get; }

    /// <summary>Номер родительской записи из ссылки лучшего имени (для детекта переработанного родителя).</summary>
    public ulong? ParentRecordNumber { get; }

    /// <summary>Sequence родительской записи — несовпадение означает переработанный родитель.</summary>
    public ushort? ParentSequence { get; }

    /// <summary>Выделенный размер из неименованного $DATA (нерезидентного); 0 для резидентного/каталога.</summary>
    public ulong AllocatedSize { get; }

    /// <summary>Реальный размер данных из неименованного $DATA.</summary>
    public ulong RealSize { get; }

    /// <summary>Флаги записи (InUse/Directory).</summary>
    public MftRecordFlags Flags { get; }

    /// <summary>Жёсткие связи сверх канонической: max(0, linkCount − 1). День 8 не считает дважды.</summary>
    public int ExtraLinks { get; }

    /// <summary>Запись содержит $ATTRIBUTE_LIST (0x20) — атрибуты могут лежать в extension-записях.</summary>
    public bool HasAttributeList { get; }

    /// <summary>Ошибка разбора; null = запись разобрана успешно (в т.ч. как существующая).</summary>
    public MftRecordError? Error { get; }
}
