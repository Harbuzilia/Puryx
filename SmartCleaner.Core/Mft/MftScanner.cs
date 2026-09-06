using System.IO;

namespace SmartCleaner.Core.Mft;

public class MftScanner
{
    private readonly MftReader _reader = new();

    public async Task<List<MftEntry>> ScanDriveAsync(string driveLetter, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            progress?.Report($"Инициализация MFT сканера для диска {driveLetter}...");
            return _reader.ReadVolumeEntries(driveLetter, progress, ct);
        }, ct);
    }
}
