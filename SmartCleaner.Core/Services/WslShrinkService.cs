using System.Diagnostics;
using System.IO;

namespace SmartCleaner.Core.Services;

/// <summary>
/// Сервис сжатия виртуальных дисков WSL2 (ext4.vhdx) через diskpart compact
/// </summary>
public class WslShrinkService
{
    public async Task<(bool Success, string Message, long SpaceSavedBytes)> ShrinkWslDiskAsync(string vhdxPath, IProgress<string>? progress = null)
    {
        if (!File.Exists(vhdxPath))
        {
            return (false, $"Файл виртуального диска не найден: {vhdxPath}", 0);
        }

        var initialSize = new FileInfo(vhdxPath).Length;
        progress?.Report("Остановка фоновых инстансов WSL (wsl --shutdown)...");

        try
        {
            // 1. Shutdown WSL to unlock the VHDX file
            var shutdownPsi = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = "--shutdown",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var shutdownProc = Process.Start(shutdownPsi);
            if (shutdownProc != null)
            {
                await shutdownProc.WaitForExitAsync();
            }

            // Wait a moment for file lock release
            await Task.Delay(1500);

            progress?.Report("Запуск сжатия VHDX через DiskPart...");

            // 2. Prepare DiskPart script
            var tempScript = Path.Combine(Path.GetTempPath(), $"compact_wsl_{Guid.NewGuid():N}.txt");
            var scriptContent = $"select vdisk file=\"{vhdxPath}\"\r\ncompact vdisk\r\n";
            await File.WriteAllTextAsync(tempScript, scriptContent);

            var diskpartPsi = new ProcessStartInfo
            {
                FileName = "diskpart.exe",
                Arguments = $"/s \"{tempScript}\"",
                CreateNoWindow = true,
                UseShellExecute = true, // elevated if needed
                Verb = "runas"
            };

            var diskpartProc = Process.Start(diskpartPsi);
            if (diskpartProc != null)
            {
                await diskpartProc.WaitForExitAsync();
            }

            try { File.Delete(tempScript); } catch (Exception ex) { Debug.WriteLine($"[WslShrinkService] Temp script cleanup error: {ex.Message}"); }

            var newSize = new FileInfo(vhdxPath).Length;
            var saved = Math.Max(0, initialSize - newSize);

            return (true, $"Сжатие завершено! Размер: {Helpers.SizeFormatter.Format(initialSize)} -> {Helpers.SizeFormatter.Format(newSize)} (Освобождено: {Helpers.SizeFormatter.Format(saved)})", saved);
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка при сжатии WSL диска: {ex.Message}", 0);
        }
    }
}
