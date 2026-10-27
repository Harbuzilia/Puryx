using System.Diagnostics;
using System.Globalization;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Mft;
using Xunit;
using Xunit.Abstractions;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 10 (ROADMAP v3.1, фаза M): бенчмарк-приёмка MFT на живом системном
/// диске против эквивалентного directory-walk (Category=Integration:
/// машинозависим — объём тома, фрагментация $MFT, права процесса; в базовый
/// гейт и CI не входит). Ассерты — только контракт приёмки (скан завершается,
/// записей на живом системном томе &gt; 100k); цель-ориентир «MFT &lt; 15 с
/// на томах ≥ 200 ГБ» числом НЕ ассертится — фактическое время фиксируется
/// в вывод теста (уходит в CHANGELOG), подгонки ассертов нет. Отказ прав —
/// честная SKIP-семантика: тест не падает, числа требует elevated-запуска.
/// </summary>
[Trait("Category", "Integration")]
public class MftBenchmarkTests : IDisposable
{
    private static readonly TimeSpan OverallGuard = TimeSpan.FromMinutes(5);   // страховка зависания теста
    private static readonly TimeSpan FullWalkBudget = TimeSpan.FromMinutes(2); // дальше — поддерево + экстраполяция

    /// <summary>Перечисление как у реального walk-сканера: недоступные записи пропускаются,
    /// Hidden/System не отсеиваются (покрытие как у MFT), рекурсия ручная (отменяемая).</summary>
    private static readonly EnumerationOptions WalkEnumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
    };

    private readonly ITestOutputHelper _output;
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public MftBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        Localizer.Culture = CultureInfo.InvariantCulture; // EN-тексты: ASCII в консоли
    }

    public void Dispose()
    {
        Localizer.Culture = _originalLocalizerCulture;
    }

    [Fact]
    public void TryScanDrive_VsDirectoryWalk_SystemDrive_Benchmark()
    {
        // системный диск (где ОС; temp не подходит — TMP может быть перенаправлен)
        string systemRoot = Path.GetPathRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.System))!;
        Assert.False(string.IsNullOrEmpty(systemRoot), "system root not found");

        var drive = new DriveInfo(systemRoot);
        _output.WriteLine(
            $"benchmark drive {drive.Name}: {drive.DriveFormat}, {drive.TotalSize / 1024.0 / 1024 / 1024:F1} GiB total, {drive.TotalFreeSpace / 1024.0 / 1024 / 1024:F1} GiB free");

        var scanner = new MftDiskScanner(); // без конфиг-сервиса → Auto
        using var guard = new CancellationTokenSource(OverallGuard);

        MftScanResult result;
        try
        {
            result = scanner.TryScanDrive(systemRoot, guard.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail(
                $"MFT scan exceeded the {OverallGuard.TotalMinutes:F0}-min overall guard (benchmark safety net)");
            return;
        }

        if (!result.Success)
        {
            // нет прав / том не открылся: SKIP-семантика (не падение), числа — в elevated-запуске
            _output.WriteLine(
                $"SKIP: MFT mode unavailable on {drive.Name} ({result.FailureKind}); benchmark numbers require an elevated run");
            Assert.NotEqual(MftScanFailureKind.Disabled, result.FailureKind);
            Assert.NotEqual(MftScanFailureKind.None, result.FailureKind);
            Assert.False(string.IsNullOrEmpty(result.FailureText));
            if (result.FailureKind == MftScanFailureKind.AccessDenied)
            {
                Assert.Contains("administrator", result.FailureText, StringComparison.OrdinalIgnoreCase);
            }
            return;
        }

        // ----- ветка MFT: контракт приёмки (числа — в вывод, не в ассерты) -----
        Assert.Equal("MFT", result.Mode);
        Assert.NotNull(result.Tree);
        Assert.True(result.RecordCount > 100_000,
            $"record count on live system volume too small: {result.RecordCount:N0}");
        long liveNodes = result.Tree!.Nodes.Count;
        Assert.True(liveNodes > 0, "MFT tree is empty");

        double mftSeconds = result.Elapsed.TotalSeconds;
        _output.WriteLine(
            $"MFT scan: {result.RecordCount:N0} records, {liveNodes:N0} live tree nodes in {mftSeconds:F2} s ({result.RecordCount / mftSeconds / 1000.0:F1}k records/s)");
        if (drive.TotalSize >= 200L * 1024 * 1024 * 1024)
        {
            _output.WriteLine(
                $"target reference (volume >= 200 GiB): MFT < 15 s -> actual {mftSeconds:F2} s (recorded, not asserted)");
        }

        // ----- walk-эквивалент: самое дешёвое полное перечисление имен -----
        WalkStats fullWalk = WalkCount(systemRoot, FullWalkBudget, guard.Token);
        if (fullWalk.Completed)
        {
            double walkSeconds = fullWalk.Elapsed.TotalSeconds;
            _output.WriteLine(
                $"walk: {fullWalk.Entries:N0} entries ({fullWalk.Directories:N0} dirs) in {walkSeconds:F2} s ({fullWalk.Entries / walkSeconds / 1000.0:F1}k entries/s) — full volume, completed within the {FullWalkBudget.TotalMinutes:F0}-min budget");
            if (walkSeconds > mftSeconds)
            {
                _output.WriteLine(
                    $"walk/MFT ratio: {walkSeconds / mftSeconds:F1}x (walk slower than MFT)");
            }
        }
        else
        {
            _output.WriteLine(
                $"walk: full-volume attempt abandoned after {fullWalk.Elapsed.TotalSeconds:F0} s (partial {fullWalk.Entries:N0} entries) — budget {FullWalkBudget.TotalMinutes:F0} min");
            WalkSubtreeAndExtrapolate(systemRoot, liveNodes, guard.Token);
        }
    }

    /// <summary>
    /// Полный обход не уложился в бюджет: замер walk на репрезентативном
    /// поддереве (Users) и честная экстраполяция по live-узлам MFT —
    /// методика печатается, чисел без замера нет.
    /// </summary>
    private void WalkSubtreeAndExtrapolate(string systemRoot, long liveNodes, CancellationToken ct)
    {
        string subtree = Path.Combine(systemRoot, "Users");
        if (!Directory.Exists(subtree))
        {
            _output.WriteLine(
                "walk: representative subtree (Users) not found — no walk numbers, extrapolation skipped (no invented data)");
            return;
        }

        WalkStats subtreeWalk = WalkCount(subtree, FullWalkBudget, ct);
        if (!subtreeWalk.Completed || subtreeWalk.Elapsed.TotalSeconds < 1)
        {
            _output.WriteLine(
                $"walk: subtree {subtree} enumeration did not finish (or too fast for a representative rate) — no walk numbers, extrapolation skipped (no invented data)");
            return;
        }

        double rate = subtreeWalk.Entries / subtreeWalk.Elapsed.TotalSeconds;
        double extrapolated = liveNodes / rate;
        _output.WriteLine(
            $"walk: subtree {subtree} = {subtreeWalk.Entries:N0} entries in {subtreeWalk.Elapsed.TotalSeconds:F2} s -> rate {rate / 1000.0:F1}k entries/s");
        _output.WriteLine(
            $"walk extrapolation: {liveNodes:N0} live MFT nodes / subtree rate ~ {extrapolated:F0} s (methodology: rate from {subtree}, coverage = live entries, name-only enumeration)");
    }

    private readonly record struct WalkStats(long Entries, long Directories, TimeSpan Elapsed, bool Completed);

    /// <summary>
    /// Считает записи рекурсивным обходом (DFS на стеке, глубина не рекурсия):
    /// DirectoryInfo.EnumerateFileSystemInfos — классификация файл/каталог из
    /// find-data без лишнего syscall на запись; недоступные каталоги
    /// (права/транзиент) пропускаются как в реальном walk-сканере. Бюджет —
    /// честная остановка (Completed=false), выдуманных чисел нет.
    /// </summary>
    private static WalkStats WalkCount(string root, TimeSpan budget, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        long entries = 0, directories = 0;
        bool completed = true;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(root));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= budget)
            {
                completed = false;
                break;
            }

            DirectoryInfo dir = pending.Pop();
            directories++;
            try
            {
                foreach (FileSystemInfo child in dir.EnumerateFileSystemInfos("*", WalkEnumeration))
                {
                    entries++;
                    // junction/symlink не рекурсируем — как walk-сканеры продукта
                    // (DiskUsage/DiskMap/LargeFiles): иначе циклы и двойной счёт
                    if (child is DirectoryInfo sub
                        && (sub.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(sub);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // недоступный/исчезнувший каталог: посчитанное сохранено, пропускаем
            }
        }

        stopwatch.Stop();
        return new WalkStats(entries, directories, stopwatch.Elapsed, completed);
    }
}
