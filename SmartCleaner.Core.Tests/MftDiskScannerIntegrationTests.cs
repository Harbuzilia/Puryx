using System.Globalization;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 9 (ROADMAP v3.1, фаза M): интеграционная приёмка MftDiskScanner на
/// живом томе — реальное чтение $MFT (Category=Integration: машинозависим,
/// зависит от прав процесса; в базовый гейт и CI не входит). Обе ветки:
/// успех → конверсия выдаёт существующие пути живого тома; отказ прав →
/// классифицированная причина с подсказкой про администратора.
/// </summary>
[Trait("Category", "Integration")]
public class MftDiskScannerIntegrationTests : IDisposable
{
    private readonly CultureInfo? _originalLocalizerCulture = Localizer.Culture;

    public MftDiskScannerIntegrationTests()
    {
        Localizer.Culture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        Localizer.Culture = _originalLocalizerCulture;
    }

    [Fact]
    public void TryScanDrive_LiveVolume_EitherMftTreeOrClassifiedFailure()
    {
        // том, где лежит temp процесса (GetFullPath: TMP-обход песочницы)
        string volumeRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        var scanner = new MftDiskScanner(); // без конфиг-сервиса → Auto

        MftScanResult result = scanner.TryScanDrive(volumeRoot, CancellationToken.None);

        if (result.Success)
        {
            // успех: статистика + конверсия на живом дереве
            Assert.Equal("MFT", result.Mode);
            Assert.NotNull(result.Tree);
            Assert.NotNull(result.Tree.Root);
            Assert.True(result.RecordCount > 0);
            Assert.True(result.Elapsed > TimeSpan.Zero);

            List<LargeFiles.LargeFileInfo> files = MftDiskScanner.ToLargeFiles(
                result.Tree.Root, volumeRoot, minSize: 1024 * 1024, maxCount: 50);
            Assert.NotEmpty(files);
            Assert.All(files, f => Assert.True(File.Exists(f.Path), $"path does not exist: {f.Path}"));
        }
        else
        {
            // отказ: не «тихий null», а классифицированная локализованная причина
            Assert.NotEqual(MftScanFailureKind.Disabled, result.FailureKind);
            Assert.False(string.IsNullOrEmpty(result.FailureText));
            if (result.FailureKind == MftScanFailureKind.AccessDenied)
            {
                Assert.Contains("administrator", result.FailureText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
