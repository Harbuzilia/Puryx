using System;
using System.IO;
using System.Linq;
using System.Threading;
using SmartCleaner.Core.Mft;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 8 (ROADMAP v3.1, фаза M) — интеграционный тест на живом системном томе.
/// Обе ветки валидны и зависят от прав процесса:
/// 1) без администратора — CreateFileW на том отдаёт Win32 error 5, ридер
///    открывается, но CanRead = false с честной причиной «отказано в доступе»;
/// 2) с администратором — полный конвейер: перечисление $MFT (первые 100k
///    записей), сборка дерева, корень = запись 5, среди детей корня — «Windows».
/// Скоростные ассерты не делаются (CI-машины разные).
/// </summary>
[Trait("Category", "Integration")]
public class MftVolumeIntegrationTests
{
    [Fact]
    public void TryOpenVolume_OnSystemDrive_EitherReadsMftOrReportsAccessDenied()
    {
        // Буква системного тома из окружения: "C:\" -> 'C'.
        char drive = Path.GetPathRoot(Environment.SystemDirectory)![0];

        bool opened = MftVolume.TryOpenVolume(drive, out var reader, out var failure);

        // Системный том существует и является NTFS — принципиальных причин
        // для false быть не должно (проверяется на NTFS-машинах проекта).
        Assert.True(opened);
        Assert.NotNull(reader);
        Assert.Null(failure);

        using (reader!)
        {
            if (!reader.CanRead)
            {
                // Ветка без прав: причина обязана быть и объяснять отказ
                // понятным пользователю текстом.
                Assert.NotNull(reader.FailureReason);
                Assert.Contains("отказано в доступе", reader.FailureReason);
                return;
            }

            // Ветка с правами: полный конвейер День 8.
            Assert.Null(reader.FailureReason);
            Assert.InRange(reader.FileRecordSize, 256, 1 << 20);
            Assert.True(reader.RecordCapacity >= 1000,
                $"RecordCapacity = {reader.RecordCapacity}, ожидается >= 1000");

            var records = reader.EnumerateRecords(CancellationToken.None)
                .Take(100_000)
                .ToList();
            Assert.True(records.Count >= 1000,
                $"прочитано записей: {records.Count}, ожидается >= 1000");

            var tree = MftTreeBuilder.Build(records);
            var root = tree.Root;
            Assert.NotNull(root);
            Assert.Equal(5UL, root.RecordNumber);
            Assert.Contains(root.Children, child =>
                string.Equals(child.FileName, "Windows", StringComparison.OrdinalIgnoreCase));
        }
    }
}
