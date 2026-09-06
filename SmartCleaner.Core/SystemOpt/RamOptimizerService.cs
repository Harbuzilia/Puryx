using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SmartCleaner.Core.SystemOpt;

public struct MemoryInfo
{
    public long TotalBytes;
    public long AvailableBytes;
    public long UsedBytes;
    public double LoadPercentage;

    public string TotalFormatted => SizeFormatter.Format(TotalBytes);
    public string AvailableFormatted => SizeFormatter.Format(AvailableBytes);
    public string UsedFormatted => SizeFormatter.Format(UsedBytes);
}

public class RamOptimizationResult
{
    public MemoryInfo Before { get; set; }
    public MemoryInfo After { get; set; }
    public long ReclaimedBytes => Math.Max(0, After.AvailableBytes - Before.AvailableBytes);
    public string ReclaimedFormatted => SizeFormatter.Format(ReclaimedBytes);
    public int ProcessesOptimized { get; set; }
}

public class RamOptimizerService
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public MemoryInfo GetMemoryStatus()
    {
        var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };
        if (GlobalMemoryStatusEx(ref memStatus))
        {
            var total = (long)memStatus.ullTotalPhys;
            var avail = (long)memStatus.ullAvailPhys;
            return new MemoryInfo
            {
                TotalBytes = total,
                AvailableBytes = avail,
                UsedBytes = total - avail,
                LoadPercentage = memStatus.dwMemoryLoad
            };
        }

        return new MemoryInfo();
    }

    public async Task<RamOptimizationResult> OptimizeMemoryAsync(IProgress<string>? progress = null)
    {
        var result = new RamOptimizationResult
        {
            Before = GetMemoryStatus()
        };

        progress?.Report("Оптимизация рабочих наборов процессов и очистка кэша памяти...");

        int count = 0;
        await Task.Run(() =>
        {
            var currentPid = Process.GetCurrentProcess().Id;
            var processes = Process.GetProcesses();

            foreach (var proc in processes)
            {
                if (proc.Id == currentPid || proc.Id <= 4) continue; // Skip self and System/Idle

                try
                {
                    if (EmptyWorkingSet(proc.Handle) != 0)
                    {
                        count++;
                    }
                }
                catch { }
                finally
                {
                    try { proc.Dispose(); } catch { }
                }
            }

            // Force GC for self
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        });

        result.ProcessesOptimized = count;
        result.After = GetMemoryStatus();
        return result;
    }
}
