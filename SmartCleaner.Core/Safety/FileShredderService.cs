using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Safety;

public enum ShredMethod
{
    QuickZero1Pass,
    DoD522022M3Pass
}

public class FileShredderService
{
    private readonly ISafetyService _safety;

    public FileShredderService(ISafetyService safety)
    {
        _safety = safety;
    }

    public async Task<(bool Success, string Message)> ShredFileAsync(string filePath, ShredMethod method = ShredMethod.DoD522022M3Pass, IProgress<string>? progress = null)
    {
        if (!File.Exists(filePath))
            return (false, "Файл не найден");

        // Безвозвратное удаление — обязательный whitelist-гейт.
        // PerformanceCache: явное действие пользователя, защищённый период не применяется,
        // но whitelist (.git, сохранения, конфиги AI) и блокировки действуют
        var validation = _safety.ValidateForDeletion(new ScannedItem
        {
            Path = filePath,
            Size = new FileInfo(filePath).Length,
            Risk = RiskCategory.PerformanceCache,
            Description = "shred:manual"
        });

        if (!validation.CanDelete)
        {
            return (false, $"Заблокировано: {validation.BlockReason}");
        }

        if (validation.RequiresElevation)
        {
            return (false, "Файл требует прав администратора");
        }

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

        // Каталог удаляем только если ни один файл не был заблокирован safety-гейтом —
        // иначе recursive-delete снёс бы и защищённые файлы
        if (errors.Count == 0)
        {
            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch (Exception ex) { Debug.WriteLine($"[FileShredderService] ShredDirectory cleanup error: {ex.Message}"); }
        }

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
