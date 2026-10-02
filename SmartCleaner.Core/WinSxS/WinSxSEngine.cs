using SmartCleaner.Core.Helpers;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SmartCleaner.Core.WinSxS;

public class WinSxSAnalysisResult
{
    public long WindowsReportedSizeBytes { get; set; }
    public string WindowsReportedSizeFormatted { get; set; } = "н/д";
    public long ActualSizeBytes { get; set; }
    public string ActualSizeFormatted { get; set; } = "н/д";
    public long SharedWithWindowsBytes { get; set; }
    public long BackupsAndDisabledFeaturesBytes { get; set; }
    public long ReclaimablePackagesBytes { get; set; }
    public string ReclaimablePackagesFormatted { get; set; } = "н/д";
    public bool CleanupRecommended { get; set; }
    public string RawAnalysisOutput { get; set; } = string.Empty;

    /// <summary>
    /// true — значения получены из фактического вывода DISM.
    /// false — данных нет: поля размеров показывают «н/д», рекомендация не выдаётся.
    /// </summary>
    public bool AnalysisAvailable { get; set; }

    /// <summary>Причина недоступности анализа (для показа пользователю).</summary>
    public string AnalysisUnavailableReason { get; set; } = string.Empty;
}

public class WinSxSEngine
{
    public async Task<WinSxSAnalysisResult> AnalyzeComponentStoreAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var result = new WinSxSAnalysisResult();
        progress?.Report("Анализ хранилища компонентов WinSxS через DISM...");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetDismPath(),
                Arguments = "/Online /Cleanup-Image /AnalyzeComponentStore",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                result.RawAnalysisOutput = "Не удалось запустить DISM";
                result.AnalysisUnavailableReason = "Не удалось запустить DISM";
                return result;
            }

            // Потоки читаем до ожидания выхода, чтобы заполненный буфер не заблокировал DISM
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            // DISM печатает результат в stdout; при пустом stdout — пробуем stderr
            result = ParseAnalyzeComponentStoreOutput(stdout.Length > 0 ? stdout : stderr);

            if (!result.AnalysisAvailable && proc.ExitCode != 0)
                result.AnalysisUnavailableReason = $"{result.AnalysisUnavailableReason} (код возврата {proc.ExitCode})";
        }
        catch (Exception ex)
        {
            result.RawAnalysisOutput = $"Ошибка анализа DISM: {ex.Message}";
            result.AnalysisUnavailableReason = $"Ошибка анализа DISM: {ex.Message}";
        }

        if (!result.AnalysisAvailable)
            progress?.Report($"WinSxS: {result.AnalysisUnavailableReason}");

        return result;
    }

    /// <summary>
    /// Разбор вывода «dism /Online /Cleanup-Image /AnalyzeComponentStore»: en- и ru-локализация,
    /// десятичный разделитель «.»/«,», единицы B/KB/MB/GB/TB и Б/КБ/МБ/ГБ/ТБ.
    /// Чистая функция без выдуманных значений: если разбор не удался — AnalysisAvailable=false,
    /// размеры остаются «н/д», рекомендация очистки не выставляется.
    /// </summary>
    internal static WinSxSAnalysisResult ParseAnalyzeComponentStoreOutput(string output)
    {
        var result = new WinSxSAnalysisResult { RawAnalysisOutput = output };

        if (string.IsNullOrWhiteSpace(output))
        {
            result.AnalysisUnavailableReason = "DISM не вернул данных";
            return result;
        }

        if (LooksLikeElevationFailure(output))
        {
            result.AnalysisUnavailableReason = "Анализ недоступен (нужны права администратора)";
            return result;
        }

        result.ActualSizeBytes = ParseSize(output, "Actual Size of Component Store", "Фактический размер хранилища компонентов") ?? 0;
        result.SharedWithWindowsBytes = ParseSize(output, "Shared with Windows", "Общий с Windows") ?? 0;
        result.BackupsAndDisabledFeaturesBytes = ParseSize(output, "Backups and Disabled Features", "Резервные копии и отключенные компоненты") ?? 0;
        result.ReclaimablePackagesBytes = result.BackupsAndDisabledFeaturesBytes;

        result.CleanupRecommended =
            output.Contains("Component Store Cleanup Recommended : Yes", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Рекомендуется очистка хранилища компонентов : Да", StringComparison.OrdinalIgnoreCase);

        if (result.ActualSizeBytes <= 0)
        {
            result.AnalysisUnavailableReason = "Не удалось разобрать вывод DISM: данные о размере хранилища не найдены";
            return result;
        }

        result.AnalysisAvailable = true;
        result.ActualSizeFormatted = SizeFormatter.Format(result.ActualSizeBytes);
        result.ReclaimablePackagesFormatted = result.ReclaimablePackagesBytes > 0
            ? SizeFormatter.Format(result.ReclaimablePackagesBytes)
            : "н/д";
        return result;
    }

    // DISM /AnalyzeComponentStore требует повышения прав: без него ошибка 740
    // «The requested operation requires elevation» (ru: «...требует повышения»).
    // Именно её показываем пользователю, а не безликое «нет данных».
    private static bool LooksLikeElevationFailure(string output) =>
        Regex.IsMatch(output, @"Error\D*740\b", RegexOptions.IgnoreCase) ||
        output.Contains("requires elevation", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("повышен", StringComparison.OrdinalIgnoreCase);

    // Одна строка «<label> : <число> <единица>» в en- или ru-локали; null — строка не найдена
    private static long? ParseSize(string output, string labelEn, string labelRu)
    {
        var match = Regex.Match(
            output,
            $"(?:{labelEn}|{labelRu})\\s*:\\s*(\\d+(?:[.,]\\d+)?)\\s*([KMGT]?B|[КМГТ]?Б)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        // «8,12» (ru) и «8.12» (en) → инвариантная точка
        var number = match.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        return (long)(value * UnitMultiplier(match.Groups[2].Value));
    }

    private static double UnitMultiplier(string unit) => unit.ToUpperInvariant() switch
    {
        "GB" or "ГБ" => 1024.0 * 1024 * 1024,
        "MB" or "МБ" => 1024.0 * 1024,
        "KB" or "КБ" => 1024.0,
        "TB" or "ТБ" => 1024.0 * 1024 * 1024 * 1024,
        _ => 1.0 // B / Б
    };

    public async Task<(bool Success, string Message)> RunComponentCleanupAsync(bool resetBase = true, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Запуск очистки WinSxS (DISM StartComponentCleanup)...");

        var args = resetBase
            ? "/Online /Cleanup-Image /StartComponentCleanup /ResetBase"
            : "/Online /Cleanup-Image /StartComponentCleanup";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetDismPath(),
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas" // elevated
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync(ct);
                return (proc.ExitCode == 0, $"Очистка WinSxS завершена с кодом {proc.ExitCode}");
            }
            return (false, "Не удалось запустить DISM");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка выполнения DISM: {ex.Message}");
        }
    }
}
