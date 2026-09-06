using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Runtime.InteropServices;

namespace SmartCleaner.Core.Mft;

public class MftReader
{
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FSCTL_ENUM_USN_DATA = 0x000900b3;
    private const uint FSCTL_QUERY_USN_JOURNAL = 0x000900f4;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    public bool IsNtfsVolume(string driveLetter)
    {
        try
        {
            var drive = new DriveInfo(driveLetter);
            return drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public List<MftEntry> ReadVolumeEntries(string driveLetter, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var entries = new List<MftEntry>();
        var rootDrive = driveLetter.TrimEnd('\\').ToUpperInvariant();
        if (!rootDrive.EndsWith(":")) rootDrive += ":";
        var volumePath = $@"\\.\{rootDrive}";

        using var handle = CreateFile(
            volumePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            // Requires elevation or volume inaccessible, fallback to fast scanner
            return FastMultiThreadedScan(driveLetter, progress, ct);
        }

        // Fast reading via buffer
        var bufferSize = 64 * 1024;
        var buffer = Marshal.AllocHGlobal(bufferSize);

        try
        {
            var med = new MFT_ENUM_DATA { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = long.MaxValue };
            var medSize = Marshal.SizeOf(med);
            var medBuffer = Marshal.AllocHGlobal(medSize);
            Marshal.StructureToPtr(med, medBuffer, true);

            var entryMap = new Dictionary<ulong, MftEntry>();

            while (!ct.IsCancellationRequested)
            {
                if (!DeviceIoControl(handle, FSCTL_ENUM_USN_DATA, medBuffer, (uint)medSize, buffer, (uint)bufferSize, out uint bytesReturned, IntPtr.Zero) || bytesReturned <= 8)
                {
                    break;
                }

                var nextUsn = Marshal.ReadInt64(buffer);
                Marshal.WriteInt64(medBuffer, 0, nextUsn);

                var offset = 8;
                while (offset < bytesReturned)
                {
                    var recordLen = Marshal.ReadInt32(buffer, offset);
                    if (recordLen <= 0) break;

                    var fileRef = (ulong)Marshal.ReadInt64(buffer, offset + 8);
                    var parentRef = (ulong)Marshal.ReadInt64(buffer, offset + 16);
                    var fileAttributes = (uint)Marshal.ReadInt32(buffer, offset + 52);
                    var fileNameLen = Marshal.ReadInt16(buffer, offset + 56);
                    var fileNameOffset = Marshal.ReadInt16(buffer, offset + 58);

                    var name = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + fileNameOffset), fileNameLen / 2);
                    var isDir = (fileAttributes & 0x10) != 0;

                    var entry = new MftEntry
                    {
                        FileReferenceNumber = fileRef,
                        ParentFileReferenceNumber = parentRef,
                        FileName = name ?? "",
                        IsDirectory = isDir
                    };

                    entryMap[fileRef] = entry;
                    offset += recordLen;
                }
            }

            Marshal.FreeHGlobal(medBuffer);

            // Reconstruct full paths
            var rootPrefix = driveLetter.TrimEnd('\\') + "\\";
            foreach (var kv in entryMap)
            {
                var current = kv.Value;
                var parts = new List<string> { current.FileName };
                var pRef = current.ParentFileReferenceNumber;

                int depth = 0;
                while (pRef != 0 && entryMap.TryGetValue(pRef, out var parent) && depth < 50)
                {
                    if (!string.IsNullOrWhiteSpace(parent.FileName))
                    {
                        parts.Add(parent.FileName);
                    }
                    pRef = parent.ParentFileReferenceNumber;
                    depth++;
                }

                parts.Reverse();
                current.FullPath = rootPrefix + string.Join("\\", parts);
                entries.Add(current);
            }

            return entries;
        }
        catch
        {
            return FastMultiThreadedScan(driveLetter, progress, ct);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public List<MftEntry> FastMultiThreadedScan(string rootPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var entries = new List<MftEntry>();
        if (!Directory.Exists(rootPath)) return entries;

        progress?.Report($"Выполняется скоростное сканирование директорий {rootPath}...");

        var dirsToScan = new Queue<string>();
        dirsToScan.Enqueue(rootPath);

        while (dirsToScan.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = dirsToScan.Dequeue();

            try
            {
                var dirInfo = new DirectoryInfo(current);
                foreach (var fi in dirInfo.EnumerateFiles())
                {
                    entries.Add(new MftEntry
                    {
                        FileName = fi.Name,
                        FullPath = fi.FullName,
                        FileSizeBytes = fi.Length,
                        IsDirectory = false,
                        LastWriteTime = fi.LastWriteTime
                    });
                }

                foreach (var di in dirInfo.EnumerateDirectories())
                {
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue; // Skip symlinks/junctions
                    dirsToScan.Enqueue(di.FullName);
                    entries.Add(new MftEntry
                    {
                        FileName = di.Name,
                        FullPath = di.FullName,
                        FileSizeBytes = 0,
                        IsDirectory = true,
                        LastWriteTime = di.LastWriteTime
                    });
                }
            }
            catch { }
        }

        return entries;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MFT_ENUM_DATA
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }
}
