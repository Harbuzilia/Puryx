namespace SmartCleaner.Core.Mft;

/// <summary>
/// Сырая MFT-запись, прочитанная с тома (День 8): номер по позиции в $MFT
/// и байты. При <see cref="FixupApplied"/> = true байты восстановлены
/// (USA применён либо не требовался — пустой/битый слот) и готовы к
/// <see cref="MftParser.ParseRecord"/>; false — дисковая форма после
/// непрекращающегося torn read, разбор недостоверен.
/// </summary>
/// <remarks>
/// Стриминговый контракт памяти: <see cref="Bytes"/> указывает во внутренний
/// переиспользуемый буфер ридера и действителен ТОЛЬКО до следующей итерации
/// перечисления (MoveNext) — копируйте при необходимости удержания.
/// </remarks>
public readonly struct MftRawRecord
{
    internal MftRawRecord(long recordNumber, ReadOnlyMemory<byte> bytes, bool fixupApplied)
    {
        RecordNumber = recordNumber;
        Bytes = bytes;
        FixupApplied = fixupApplied;
    }

    /// <summary>Номер записи = позиция слота в $MFT (0 — сама запись $MFT).</summary>
    public long RecordNumber { get; }

    /// <summary>Байты записи; действительны до следующей итерации — см. remarks.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    /// <summary>true — USA-фиксап применён, байты можно разбирать; false — torn read.</summary>
    public bool FixupApplied { get; }
}

/// <summary>
/// Классификация отказа ридера тома (День 9, ROADMAP v3.1): машиночитаемая
/// причина фолбэка MFT-режима — UI показывает локализованный текст по виду
/// отказа (права → подсказка про администратора, не NTFS → обычный обход).
/// </summary>
public enum MftReaderFailure
{
    /// <summary>$MFT читается — отказа нет.</summary>
    None = 0,

    /// <summary>Отказ прав (Win32 error 5): чтение $MFT требует администратора.</summary>
    AccessDenied,

    /// <summary>Том не NTFS или принципиально не открылся.</summary>
    NotNtfs,

    /// <summary>Структурная проблема тома (запись $MFT нечитаема/повреждена и т.п.).</summary>
    VolumeError
}

/// <summary>
/// Чтение $MFT тома NTFS (День 8, ROADMAP v3.1). ТОЛЬКО ЧТЕНИЕ: том
/// открывается с GENERIC_READ, флагов записи нет. Доступ может быть
/// отказан (Win32 error 5) — тогда <see cref="CanRead"/> = false и
/// <see cref="FailureReason"/> честно объясняет причину. Перечисления
/// однопоточные и одноразовые; повторный вызов создаёт новое перечисление.
/// </summary>
public interface IMftVolumeReader : IDisposable
{
    /// <summary>true — $MFT доступна для перечисления; иначе см. <see cref="FailureReason"/>.</summary>
    bool CanRead { get; }

    /// <summary>Причина недоступности $MFT (русский текст движка для UI); null при CanRead.</summary>
    string? FailureReason { get; }

    /// <summary>Классификация отказа (День 9): None при CanRead, иначе вид причины.</summary>
    MftReaderFailure FailureKind { get; }

    /// <summary>Геометрия тома из загрузочного сектора (default при отказе доступа).</summary>
    NtfsBootSector Boot { get; }

    /// <summary>Размер MFT-записи в байтах (поле 0x1C записи 0); 0 при CanRead = false.</summary>
    int FileRecordSize { get; }

    /// <summary>Ёмкость $MFT в записях по рунам $DATA; 0 при CanRead = false.</summary>
    long RecordCapacity { get; }

    /// <summary>
    /// Перечисляет сырые записи с 0 до ёмкости: чтение чанками (один
    /// переиспользуемый буфер), USA-фиксап in place, torn-записи
    /// перечитываются (до 2 раз). Байты действительны до следующей
    /// итерации (буфер переиспользуется — см. <see cref="MftRawRecord"/>).
    /// </summary>
    /// <param name="ct">Проверяется между записями.</param>
    IEnumerable<MftRawRecord> EnumerateRawRecords(CancellationToken ct = default);

    /// <summary>
    /// Перечисляет разобранные записи (конвейер read → fixup → parse).
    /// Torn-записи возвращаются с Error = TornRead и номером по позиции;
    /// пустые слоты — NotARecord. Self-референс записи сверяется с
    /// позицией слота: расхождение → Malformed.
    /// </summary>
    /// <param name="ct">Проверяется между записями.</param>
    IEnumerable<MftRecord> EnumerateRecords(CancellationToken ct = default);
}

/// <summary>
/// Фабрика ридера тома (День 8). Возвращает false ТОЛЬКО когда том
/// принципиально не открывается: недопустимая буква, диск отсутствует,
/// загрузочный сектор не NTFS. Отказ ПРАВ (Win32 error 5) — не ошибка
/// открытия: возвращается ридер с честным CanRead = false и текстом
/// причины (локализация через Localizer — День 9).
/// </summary>
public static class MftVolume
{
    /// <summary>Открывает том по букве (регистронезависимо, A–Z) для чтения $MFT.</summary>
    /// <param name="volumeLetter">Буква тома, например 'C'.</param>
    /// <param name="reader">Ридер (в т.ч. в состоянии отказа прав) или null при false.</param>
    /// <param name="failure">Причина принципиальной неудачи (русский текст) или null.</param>
    public static bool TryOpenVolume(char volumeLetter, out IMftVolumeReader? reader, out string? failure)
    {
        reader = null;
        failure = null;

        char letter = char.ToUpperInvariant(volumeLetter);
        if (letter is < 'A' or > 'Z')
        {
            failure = $"недопустимая буква тома: '{volumeLetter}'";
            return false;
        }

        return DirectVolumeReader.TryCreate(letter, out reader, out failure);
    }
}
