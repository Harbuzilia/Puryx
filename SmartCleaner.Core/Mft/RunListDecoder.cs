namespace SmartCleaner.Core.Mft;

/// <summary>
/// Экстент данных $DATA: непрерывный отрезок VCN (длиной в кластеры) с
/// адресом на диске. Lcn = 0 — спарс-экстент (кластеры не выделены,
/// читаются как нули); включается в список ради VCN-непрерывности,
/// потребитель обязан его пропустить.
/// </summary>
public readonly struct RunExtent
{
    internal RunExtent(long startVcn, long startLcn, long lengthClusters)
    {
        StartVcn = startVcn;
        StartLcn = startLcn;
        LengthClusters = lengthClusters;
    }

    /// <summary>Первый VCN экстента (кумулятивная длина предыдущих ранов).</summary>
    public long StartVcn { get; }

    /// <summary>Первый LCN экстента; 0 = спарс.</summary>
    public long StartLcn { get; }

    /// <summary>Длина экстента в кластерах (&gt; 0).</summary>
    public long LengthClusters { get; }

    /// <summary>true, если экстент спарс (Lcn = 0): данные физически отсутствуют.</summary>
    public bool IsSparse => StartLcn == 0;
}

/// <summary>
/// Декодер run-list NTFS (День 8, ROADMAP v3.1): последовательность ранов,
/// каждый — заголовок (младший ниббл: размер счётчика; старший: размер
/// смещения), счётчик кластеров (LE) и знаковое смещение к предыдущему LCN
/// (LE, two's complement, кумулятивно). Размер смещения 0 = спарс-ран
/// (Lcn = 0, аккумулятор LCN не двигается). Pure-функция: порча (нулевой
/// размер счётчика, размер поля &gt; 8, усечение, счётчик 0, LCN вне
/// диапазона) → false; отсутствие терминатора 0x00 в конце — не ошибка.
/// </summary>
public static class RunListDecoder
{
    private const int MaxFieldSize = 8;

    /// <summary>
    /// Разбирает run-list в список экстентов по порядку VCN. Спарс-раны
    /// включаются с Lcn = 0 (VCN-непрерывность); данные — ненадёжные,
    /// порча → false, пустой out-список.
    /// </summary>
    /// <param name="runList">Байты ранов — от первого заголовка до терминатора 0x00 или конца.</param>
    /// <param name="extents">Экстенты по порядку VCN; пустой при успехе без ранов.</param>
    public static bool TryDecode(ReadOnlySpan<byte> runList, out IReadOnlyList<RunExtent> extents)
    {
        extents = Array.Empty<RunExtent>();
        var result = new List<RunExtent>();

        long currentVcn = 0;
        long currentLcn = 0;
        int offset = 0;

        while (offset < runList.Length)
        {
            byte header = runList[offset];
            if (header == 0)
                break; // терминатор

            int countSize = header & 0x0F;
            int fieldSize = header >> 4;
            if (countSize == 0 || countSize > MaxFieldSize || fieldSize > MaxFieldSize)
                return false;

            int fieldEnd = offset + 1 + countSize + fieldSize;
            if (fieldEnd > runList.Length)
                return false; // усечение

            ulong rawCount = ReadUnsignedLittleEndian(runList.Slice(offset + 1, countSize));
            if (rawCount == 0 || rawCount > (ulong)long.MaxValue)
                return false;
            long count = (long)rawCount;

            bool sparse = fieldSize == 0;
            if (!sparse)
            {
                long lcnDelta = ReadSignedLittleEndian(runList.Slice(offset + 1 + countSize, fieldSize));
                long startLcn;
                try
                {
                    startLcn = checked(currentLcn + lcnDelta);
                }
                catch (OverflowException)
                {
                    return false; // LCN за пределами знакового диапазона
                }
                if (startLcn < 0)
                    return false; // LCN не бывает отрицательным
                currentLcn = startLcn;
            }
            // спарс: LCN-аккумулятор не меняется

            long nextVcn;
            try
            {
                nextVcn = checked(currentVcn + count);
            }
            catch (OverflowException)
            {
                return false;
            }
            if (nextVcn < 0)
                return false;

            result.Add(new RunExtent(currentVcn, sparse ? 0 : currentLcn, count));
            currentVcn = nextVcn;
            offset = fieldEnd;
        }

        extents = result;
        return true;
    }

    private static ulong ReadUnsignedLittleEndian(ReadOnlySpan<byte> field)
    {
        ulong value = 0;
        for (int i = field.Length - 1; i >= 0; i--)
            value = (value << 8) | field[i];
        return value;
    }

    private static long ReadSignedLittleEndian(ReadOnlySpan<byte> field)
    {
        ulong value = ReadUnsignedLittleEndian(field);
        int bits = field.Length * 8;
        // знаковое расширение two's complement с произвольной шириной поля
        if (bits < 64 && ((value >> (bits - 1)) & 1) != 0)
            value |= ulong.MaxValue << bits;
        return (long)value;
    }
}
