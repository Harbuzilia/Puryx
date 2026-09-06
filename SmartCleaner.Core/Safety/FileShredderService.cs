using System.IO;
using System.Security.Cryptography;

namespace SmartCleaner.Core.Safety;

public enum ShredMethod
{
    QuickZero1Pass,
    DoD522022M3Pass
}

public class FileShredderService
{
    public async Task<(bool Success, string Message)> ShredFileAsync(string filePath, ShredMethod method = ShredMethod.DoD522022M3Pass, IProgress<string>? progress = null)
    {
        if (!File.Exists(filePath))
            return (false, "Файл не найден");

        try
        {
            await Task.Run(() =>
            {
                var fi = new FileInfo(filePath);
                // Remove read-only attributes
                fi.Attributes = FileAttributes.Normal;

                var length = fi.Length;
                if (length > 0)
                {
                    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[64 * 1024];

                        if (method == ShredMethod.QuickZero1Pass)
                        {
                            progress?.Report($"Стирание 1-pass (нули): {Path.GetFileName(filePath)}...");
                            Array.Clear(buffer, 0, buffer.Length);
                            WritePass(stream, buffer, length);
                        }
                        else
                        {
                            // DoD 5220.22-M 3-Pass:
                            // Pass 1: 0x00
                            progress?.Report($"Стирание DoD Pass 1/3 (нули)...");
                            Array.Clear(buffer, 0, buffer.Length);
                            stream.Position = 0;
                            WritePass(stream, buffer, length);

                            // Pass 2: 0xFF
                            progress?.Report($"Стирание DoD Pass 2/3 (0xFF)...");
                            for (int i = 0; i < buffer.Length; i++) buffer[i] = 0xFF;
                            stream.Position = 0;
                            WritePass(stream, buffer, length);

                            // Pass 3: Random Cryptographic Bytes
                            progress?.Report($"Стирание DoD Pass 3/3 (псевдослучайные байты)...");
                            stream.Position = 0;
                            long written = 0;
                            while (written < length)
                            {
                                var toWrite = (int)Math.Min(buffer.Length, length - written);
                                RandomNumberGenerator.Fill(buffer);
                                stream.Write(buffer, 0, toWrite);
                                written += toWrite;
                            }
                        }

                        stream.Flush(true);
                        stream.SetLength(0);
                    }
                }

                // Scramble filename and metadata in NTFS directory table
                var parent = Path.GetDirectoryName(filePath) ?? Path.GetTempPath();
                var scrambled = Path.Combine(parent, $"shred_{Guid.NewGuid():N}.tmp");
                File.Move(filePath, scrambled);
                File.Delete(scrambled);
            });

            return (true, "Файл успешно безвозвратно уничтожен (DoD 5220.22-M)!");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка шредера: {ex.Message}");
        }
    }

    public async Task<(int ShreddedCount, List<string> Errors)> ShredDirectoryAsync(string directoryPath, ShredMethod method = ShredMethod.DoD522022M3Pass, IProgress<string>? progress = null)
    {
        int count = 0;
        var errors = new List<string>();

        if (!Directory.Exists(directoryPath)) return (0, errors);

        var files = Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).ToList();
        foreach (var file in files)
        {
            var (ok, msg) = await ShredFileAsync(file, method, progress);
            if (ok) count++;
            else errors.Add($"{file}: {msg}");
        }

        try
        {
            Directory.Delete(directoryPath, true);
        }
        catch { }

        return (count, errors);
    }

    private static void WritePass(FileStream stream, byte[] buffer, long totalLength)
    {
        long written = 0;
        while (written < totalLength)
        {
            var toWrite = (int)Math.Min(buffer.Length, totalLength - written);
            stream.Write(buffer, 0, toWrite);
            written += toWrite;
        }
    }
}
