using SmartCleaner.Core.Helpers;
using System.IO;
using System.Timers;

namespace SmartCleaner.App.Services;

public class DiskHealthAlert
{
    public string DriveLetter { get; set; } = "C:";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercentage { get; set; }
    public bool IsLowSpaceWarning { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class TraySentinelService : IDisposable
{
    private readonly System.Timers.Timer _timer;
    public event Action<DiskHealthAlert>? LowSpaceAlertTriggered;

    public TraySentinelService()
    {
        _timer = new System.Timers.Timer(TimeSpan.FromMinutes(10).TotalMilliseconds);
        _timer.Elapsed += OnTimerElapsed;
        _timer.AutoReset = true;
    }

    public void StartMonitoring()
    {
        _timer.Start();
        CheckAllDrives();
    }

    public void StopMonitoring()
    {
        _timer.Stop();
    }

    private void OnTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        CheckAllDrives();
    }

    public List<DiskHealthAlert> CheckAllDrives()
    {
        var alerts = new List<DiskHealthAlert>();

        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);
            foreach (var drive in drives)
            {
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                var freePct = total > 0 ? ((double)free / total) * 100.0 : 100.0;
                var isLow = freePct < 12.0 || free < 10L * 1024 * 1024 * 1024; // < 12% or < 10 GB

                var alert = new DiskHealthAlert
                {
                    DriveLetter = drive.Name,
                    TotalBytes = total,
                    FreeBytes = free,
                    FreePercentage = freePct,
                    IsLowSpaceWarning = isLow,
                    Message = isLow
                        ? $"⚠️ На диске {drive.Name} осталось мало свободного места: {SizeFormatter.Format(free)} ({freePct:F1}%)!"
                        : $"Диск {drive.Name}: свободно {SizeFormatter.Format(free)} из {SizeFormatter.Format(total)} ({freePct:F1}%)"
                };

                alerts.Add(alert);

                if (isLow)
                {
                    LowSpaceAlertTriggered?.Invoke(alert);
                }
            }
        }
        catch { }

        return alerts;
    }

    public void Dispose()
    {
        _timer.Dispose();
    }
}
