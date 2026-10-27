using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartCleaner.Core.Mft;

/// <summary>
/// Прямое чтение $MFT через \\.\X: (День 8): CreateFileW с GENERIC_READ
/// (только чтение — флагов записи нет), геометрия из загрузочного сектора,
/// раны $MFT из записи 0, стриминговое чтение чанками с единственным
/// переиспользуемым буфером, USA-фиксап и torn-ретраи. Механизмы защиты
/// от порчи: разрезы ранов проверены при открытии, вычисления в checked,
/// нечитаемые регионы заполняются нулями (записи уходят в NotARecord).
/// </summary>
internal sealed class DirectVolumeReader : IMftVolumeReader
{
    private const int ChunkSizeTarget = 4 * 1024 * 1024; // размер чанка чтения $MFT
    private const int MaxReadRetries = 3;                 // попыток чтения региона
    private const int TornReadRetries = 2;                // перечитываний torn-записи
    private const int MinRecordSize = 256;
    private const int MaxRecordSize = 65536;

    private readonly FileStream? _stream;                 // null — том не открыт (отказ прав)
    private readonly IReadOnlyList<RunExtent> _extents;
    private readonly long _totalClusters;

    private byte[]? _chunk;            // единственный буфер перечисления
    private long _windowFirstVcn;      // VCN первого байта окна
    private int _windowValidBytes;     // валидных байт в окне с начала
    private byte[]? _tornBuffer;       // буфер перечитывания torn-записи

    /// <summary>Ридер в состоянии отказа: том не открыт или $MFT структурно нечитаема.</summary>
    private DirectVolumeReader(string failureReason, MftReaderFailure failureKind)
    {
        CanRead = false;
        FailureReason = failureReason;
        FailureKind = failureKind;
        Boot = default;
        FileRecordSize = 0;
        RecordCapacity = 0;
        _stream = null;
        _extents = Array.Empty<RunExtent>();
        _totalClusters = 0;
    }

    private DirectVolumeReader(
        FileStream stream,
        NtfsBootSector boot,
        int recordSize,
        long capacity,
        IReadOnlyList<RunExtent> extents)
    {
        CanRead = true;
        FailureReason = null;
        FailureKind = MftReaderFailure.None;
        _stream = stream;
        Boot = boot;
        FileRecordSize = recordSize;
        RecordCapacity = capacity;
        _extents = extents;

        long totalClusters = 0;
        foreach (RunExtent extent in extents)
            totalClusters += extent.LengthClusters;
        _totalClusters = totalClusters;
    }

    /// <inheritdoc />
    public bool CanRead { get; }

    /// <inheritdoc />
    public string? FailureReason { get; }

    /// <inheritdoc />
    public MftReaderFailure FailureKind { get; }

    /// <inheritdoc />
    public NtfsBootSector Boot { get; }

    /// <inheritdoc />
    public int FileRecordSize { get; }

    /// <inheritdoc />
    public long RecordCapacity { get; }

    /// <summary>
    /// Создаёт ридер: открывает том, читает загрузочный сектор и запись 0,
    /// определяет размер записи (0x1C записи 0 → hint BPB → эвристика),
    /// декодирует раны $DATA. false — том принципиально не открылся
    /// (диск/не NTFS); отказ прав → ридер с CanRead = false.
    /// </summary>
    internal static bool TryCreate(char letter, out IMftVolumeReader? reader, out string? failure)
    {
        reader = null;
        failure = null;

        SafeFileHandle handle = NativeMethods.CreateFileW(
            $@"\\.\{letter}:",
            NativeMethods.GenericRead,
            NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
            IntPtr.Zero,
            NativeMethods.OpenExisting,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ErrorAccessDenied)
            {
                // отказ прав — не ошибка открытия: честный CanRead = false
                reader = new DirectVolumeReader(
                    $"отказано в доступе к тому {letter}: (Win32 error {error}); чтение $MFT требует прав администратора",
                    MftReaderFailure.AccessDenied);
                return true;
            }

            failure = $"том {letter}: не удалось открыть (Win32 error {error})";
            return false;
        }

        var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1);
        if (!TryCreateCore(letter, stream, out DirectVolumeReader? created, out failure))
        {
            stream.Dispose();
            handle.Dispose();
            return false;
        }

        reader = created;
        return true;
    }

    private static bool TryCreateCore(char letter, FileStream stream, out DirectVolumeReader? reader, out string? failure)
    {
        reader = null;
        failure = null;

        // Загрузочный сектор: геометрия + признак NTFS (false-путь TryOpenVolume)
        byte[] bootSector = new byte[512];
        if (!TryReadStreamWithRetries(stream, 0, bootSector)
            || !NtfsBootSector.TryParse(bootSector, out NtfsBootSector boot))
        {
            failure = $"том {letter}: загрузочный сектор не читается или не NTFS";
            return false;
        }

        // Запись 0: размер — hint BPB, иначе эвристика (4Kn → 4096, иначе 1024)
        long mftOffset = checked(boot.MftStartLcn * boot.BytesPerCluster);
        int probeSize = boot.FileRecordSize ?? (boot.BytesPerSector == 4096 ? 4096 : 1024);
        byte[] record0 = new byte[probeSize];
        if (!TryAcquireRecord0(stream, mftOffset, record0, boot.BytesPerSector, out string? reason))
        {
            reader = new DirectVolumeReader($"том {letter}: запись $MFT нечитаема ({reason})", MftReaderFailure.VolumeError);
            return true;
        }

        // Поле 0x1C записи 0 авторитетнее hint/эвристики: расхождение → перечитать
        int headerSize = ValidateRecordSize(ReadHeaderAllocatedSize(record0));
        if (headerSize != 0 && headerSize != probeSize)
        {
            record0 = new byte[headerSize];
            if (!TryAcquireRecord0(stream, mftOffset, record0, boot.BytesPerSector, out reason))
            {
                reader = new DirectVolumeReader($"том {letter}: запись $MFT нечитаема ({reason})", MftReaderFailure.VolumeError);
                return true;
            }
        }
        int recordSize = headerSize != 0 ? headerSize : probeSize;

        MftRecord parsed0 = MftParser.ParseRecord(record0, recordSize);
        if (parsed0.Error != null)
        {
            reader = new DirectVolumeReader($"том {letter}: запись $MFT повреждена ({parsed0.Error})", MftReaderFailure.VolumeError);
            return true;
        }
        if (!MftDataRuns.TryLocate(record0, out ReadOnlySpan<byte> runList)
            || !RunListDecoder.TryDecode(runList, out IReadOnlyList<RunExtent> extents)
            || extents.Count == 0)
        {
            reader = new DirectVolumeReader($"том {letter}: run-list $MFT отсутствует или повреждён", MftReaderFailure.VolumeError);
            return true;
        }

        try
        {
            // Разрезы ранов: суммы кластеров и максимальный дисковый адрес — до long
            long totalClusters = 0;
            foreach (RunExtent extent in extents)
            {
                totalClusters = checked(totalClusters + extent.LengthClusters);
                _ = checked((extent.StartLcn + extent.LengthClusters) * boot.BytesPerCluster);
            }
            long capacity = checked(totalClusters * boot.BytesPerCluster) / recordSize;
            if (capacity <= 0)
            {
                reader = new DirectVolumeReader($"том {letter}: объём $MFT меньше одной записи", MftReaderFailure.VolumeError);
                return true;
            }

            reader = new DirectVolumeReader(stream, boot, recordSize, capacity, extents);
            return true;
        }
        catch (OverflowException)
        {
            reader = new DirectVolumeReader($"том {letter}: объём $MFT переполняет адресное пространство", MftReaderFailure.VolumeError);
            return true;
        }
    }

    /// <inheritdoc />
    public IEnumerable<MftRawRecord> EnumerateRawRecords(CancellationToken ct = default)
    {
        if (!CanRead || _stream == null)
            yield break;

        int bytesPerCluster = Boot.BytesPerCluster;
        int bytesPerSector = Boot.BytesPerSector;
        int recordSize = FileRecordSize;
        long capacity = RecordCapacity;

        // Единственный чанк-буфер: ~4 МБ, кратно кластеру, не меньше записи
        int chunkBytes = (Math.Max(ChunkSizeTarget, recordSize) + bytesPerCluster - 1)
            / bytesPerCluster * bytesPerCluster;
        _chunk = new byte[chunkBytes];
        _tornBuffer = new byte[recordSize];
        _windowFirstVcn = 0;
        _windowValidBytes = 0;

        for (long recordIndex = 0; recordIndex < capacity; recordIndex++)
        {
            ct.ThrowIfCancellationRequested();

            long dataOffset = recordIndex * recordSize;
            long windowStart = _windowFirstVcn * bytesPerCluster;
            if (dataOffset < windowStart
                || dataOffset + recordSize > windowStart + _windowValidBytes)
            {
                FillWindow(dataOffset, recordSize);
            }

            int inWindow = (int)(dataOffset - _windowFirstVcn * bytesPerCluster);
            var fixupError = MftParser.ApplyFixups(_chunk.AsSpan(inWindow, recordSize), bytesPerSector);
            if (fixupError == MftRecordError.TornRead)
            {
                // запись изменилась во время чтения: перечитываем точный диапазон
                fixupError = TryRereadAndFixup(dataOffset, inWindow, recordSize)
                    ? null
                    : MftRecordError.TornRead;
            }

            // NotARecord/Corrupt/Malformed — байты достоверны как есть (слот пуст/битый),
            // ParseRecord честно их классифицирует; недостоверен только непрекращающийся torn
            yield return new MftRawRecord(
                recordIndex,
                _chunk.AsMemory(inWindow, recordSize),
                fixupError != MftRecordError.TornRead);
        }
    }

    /// <inheritdoc />
    public IEnumerable<MftRecord> EnumerateRecords(CancellationToken ct = default)
    {
        foreach (MftRawRecord raw in EnumerateRawRecords(ct))
        {
            if (!raw.FixupApplied)
            {
                // torn read после ретраев: номер по позиции, содержимое недостоверно
                yield return new MftRecord(
                    (ulong)raw.RecordNumber, null, null, 0, null, null, 0, 0,
                    MftRecordFlags.None, 0, false, MftRecordError.TornRead);
                continue;
            }

            MftRecord record = MftParser.ParseRecord(raw.Bytes.Span, FileRecordSize);
            if (record.Error == null && record.Number != (ulong)raw.RecordNumber)
            {
                // self-референс не совпал с позицией слота — запись недостоверна
                yield return new MftRecord(
                    (ulong)raw.RecordNumber, null, null, 0, null, null, 0, 0,
                    MftRecordFlags.None, 0, false, MftRecordError.Malformed);
                continue;
            }

            yield return record;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream?.Dispose();
        _chunk = null;
        _tornBuffer = null;
    }

    // ---------- окно чанка: заполнение по ранам ----------

    /// <summary>
    /// Заполняет окно с кластера, содержащего neededOffset: минимум до
    /// neededOffset + neededLength, в целом ~4 МБ (до конца данных).
    /// Нечитаемый регион обнуляется — записи уйдут в NotARecord.
    /// </summary>
    private void FillWindow(long neededOffset, int neededLength)
    {
        int bytesPerCluster = Boot.BytesPerCluster;
        long firstVcn = neededOffset / bytesPerCluster;
        _windowFirstVcn = firstVcn;
        _windowValidBytes = 0;

        long windowStart = firstVcn * bytesPerCluster;

        long cursorVcn = firstVcn;
        while (_windowValidBytes < _chunk!.Length && cursorVcn < _totalClusters)
        {
            RunExtent extent = FindExtent(cursorVcn);
            long offsetInExtent = cursorVcn - extent.StartVcn;
            long extentRemainingClusters = extent.LengthClusters - offsetInExtent;
            long maxClustersInChunk = (_chunk.Length - _windowValidBytes) / bytesPerCluster;
            long clusters = Math.Min(extentRemainingClusters, maxClustersInChunk);
            if (clusters <= 0)
                break;
            int bytes = (int)(clusters * bytesPerCluster);

            if (extent.IsSparse)
            {
                Array.Clear(_chunk, _windowValidBytes, bytes); // спарс: нули
            }
            else
            {
                long diskOffset = checked((extent.StartLcn + offsetInExtent) * bytesPerCluster);
                if (!TryReadWithRetries(diskOffset, _chunk.AsSpan(_windowValidBytes, bytes)))
                    Array.Clear(_chunk, _windowValidBytes, bytes);
            }

            _windowValidBytes += bytes;
            cursorVcn += clusters;
        }

        // требуемое покрытие достигается по построению: запись внутри данных
        // (recordIndex < capacity), а окно заполняется до конца данных
    }

    /// <summary>Ищет экстент, содержащий VCN (раны упорядочены и непрерывны по VCN).</summary>
    private RunExtent FindExtent(long vcn)
    {
        int low = 0, high = _extents.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (_extents[mid].StartVcn <= vcn)
                low = mid;
            else
                high = mid - 1;
        }
        return _extents[low];
    }

    /// <summary>
    /// Перечитывает torn-запись в точный диапазон по VCN-карте (до 2 попыток)
    /// и применяет фиксап; успех копируется обратно в окно чанка.
    /// </summary>
    private bool TryRereadAndFixup(long dataOffset, int sliceInWindow, int recordSize)
    {
        byte[] torn = _tornBuffer!;
        for (int attempt = 0; attempt < TornReadRetries; attempt++)
        {
            if (!TryReadMftData(dataOffset, torn))
                return false;
            if (MftParser.ApplyFixups(torn, Boot.BytesPerSector) == null)
            {
                Buffer.BlockCopy(torn, 0, _chunk!, sliceInWindow, recordSize);
                return true;
            }
        }
        return false;
    }

    /// <summary>Читает диапазон байт данных $MFT (одна попытка), разрезая по ранам: спарс — нули.</summary>
    private bool TryReadMftData(long dataOffset, Span<byte> destination)
    {
        int bytesPerCluster = Boot.BytesPerCluster;
        long position = dataOffset;
        int written = 0;
        while (written < destination.Length)
        {
            long vcn = position / bytesPerCluster;
            if (vcn >= _totalClusters)
                return false;
            RunExtent extent = FindExtent(vcn);
            long offsetInExtentBytes = position - extent.StartVcn * bytesPerCluster;
            long extentRemainingBytes = extent.LengthClusters * bytesPerCluster - offsetInExtentBytes;
            int piece = (int)Math.Min(extentRemainingBytes, destination.Length - written);
            if (piece <= 0)
                return false;

            if (extent.IsSparse)
            {
                destination.Slice(written, piece).Clear();
            }
            else
            {
                long diskOffset = checked(extent.StartLcn * bytesPerCluster + offsetInExtentBytes);
                if (!TryReadOnce(diskOffset, destination.Slice(written, piece)))
                    return false;
            }

            written += piece;
            position += piece;
        }
        return true;
    }

    private bool TryReadWithRetries(long diskOffset, Span<byte> destination)
    {
        for (int attempt = 0; attempt < MaxReadRetries; attempt++)
        {
            if (TryReadOnce(diskOffset, destination))
                return true;
        }
        return false;
    }

    private bool TryReadOnce(long diskOffset, Span<byte> destination)
    {
        try
        {
            _stream!.Position = diskOffset;
            _stream.ReadExactly(destination);
            return true;
        }
        catch (IOException)
        {
            return false; // чтение сорвалось (транзиент/диск) — вызывающий решает
        }
    }

    // ---------- создание: чтение записи 0 ----------

    private static bool TryAcquireRecord0(
        FileStream stream, long offset, byte[] record, int bytesPerSector, out string? reason)
    {
        if (!TryReadStreamWithRetries(stream, offset, record))
        {
            reason = "ошибка чтения";
            return false;
        }
        if (MftParser.ApplyFixups(record, bytesPerSector) == null)
        {
            reason = null;
            return true;
        }

        // torn read: запись изменилась во время чтения — перечитываем
        for (int attempt = 0; attempt < TornReadRetries; attempt++)
        {
            if (!TryReadStreamWithRetries(stream, offset, record))
            {
                reason = "ошибка чтения";
                return false;
            }
            if (MftParser.ApplyFixups(record, bytesPerSector) == null)
            {
                reason = null;
                return true;
            }
        }

        reason = "torn read: запись изменяется во время чтения";
        return false;
    }

    private static bool TryReadStreamWithRetries(FileStream stream, long offset, Span<byte> destination)
    {
        for (int attempt = 0; attempt < MaxReadRetries; attempt++)
        {
            try
            {
                stream.Position = offset;
                stream.ReadExactly(destination);
                return true;
            }
            catch (IOException)
            {
                // повторяем
            }
        }
        return false;
    }

    private static int ReadHeaderAllocatedSize(byte[] record)
        => BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(0x1C, 4));

    /// <summary>Валидирует размер записи из 0x1C: степень двойки 256–65536; иначе 0.</summary>
    private static int ValidateRecordSize(int value)
        => value is >= MinRecordSize and <= MaxRecordSize && (value & (value - 1)) == 0 ? value : 0;

    private static class NativeMethods
    {
        internal const uint GenericRead = 0x80000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const int ErrorAccessDenied = 5;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        internal static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
