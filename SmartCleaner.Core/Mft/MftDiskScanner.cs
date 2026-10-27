using SmartCleaner.Core.DiskMap;
using SmartCleaner.Core.LargeFiles;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Mft;

/// <summary>Вид локализованной причины отказа MFT-режима (День 9, ROADMAP v3.1).</summary>
public enum MftScanFailureKind
{
    /// <summary>Отказа не было — скан успешен.</summary>
    None = 0,

    /// <summary>MFT отключён настройкой (MftScanMode = Off).</summary>
    Disabled,

    /// <summary>Том не NTFS (или не открылся): MFT неприменим.</summary>
    NotNtfs,

    /// <summary>Нет прав на чтение $MFT: нужна элевация, честный фолбэк на обход.</summary>
    AccessDenied,

    /// <summary>Ошибка чтения/структуры тома при перечислении $MFT.</summary>
    VolumeError
}

/// <summary>
/// Результат попытки MFT-скана тома (День 9): успех — дерево + статистика;
/// отказ — классифицированная локализованная причина для статуса скана.
/// </summary>
public sealed class MftScanResult
{
    /// <summary>true — Tree построено; иначе см. <see cref="FailureKind"/>/<see cref="FailureText"/>.</summary>
    public bool Success { get; init; }

    /// <summary>Дерево файлов тома (null при отказе).</summary>
    public MftTree? Tree { get; init; }

    /// <summary>Число перечисленных MFT-записей (включая пропущенные/битые).</summary>
    public long RecordCount { get; init; }

    /// <summary>Длительность перечисления + сборки дерева.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Фактический режим скана: "MFT" (маркер, не локализуется).</summary>
    public string Mode { get; init; } = "";

    /// <summary>Вид отказа; None при успехе.</summary>
    public MftScanFailureKind FailureKind { get; init; }

    /// <summary>Локализованный текст причины отказа (null при успехе).</summary>
    public string? FailureText { get; init; }
}

/// <summary>
/// Фасад MFT-сканирования (День 9, ROADMAP v3.1, фаза M): одна точка входа
/// для DiskUsageScanner/LargeFilesEngine/DiskMapEngine. Читает $MFT тома,
/// строит дерево <see cref="MftTree"/> и конвертирует его в модели сканеров
/// (pure-функции, тестируются синтетикой). Выбор режима — единая настройка
/// <see cref="ScanSettingsConfig"/> (файл <see cref="SettingsFilename"/>):
/// Auto (NTFS+права → MFT) | On | Off; неудача — честный фолбэк на обход
/// каталогов с причиной в статусе. Элевацию НЕ создаём: без прав — фолбэк
/// и подсказка; полноценный elevated-анализатор — вне v3.1.
/// </summary>
public class MftDiskScanner
{
    /// <summary>Имя файла единой настройки сканирования (единая точка для Core и App).</summary>
    public const string SettingsFilename = "scan_settings.json";

    private readonly IConfigService? _configService;

    /// <summary>DI-конструктор; null конфиг-сервиса → предпочтение Auto (только чтение).</summary>
    public MftDiskScanner(IConfigService? configService = null)
    {
        _configService = configService;
    }

    /// <summary>
    /// Пробует отсканировать том по корню пути ("C:\" или любой путь на томе):
    /// читает $MFT и строит дерево. Отказ — не исключение, а результат с
    /// классифицированной локализованной причиной.
    /// </summary>
    /// <param name="driveRoot">Корень тома или любой путь на нём (буква из корня).</param>
    /// <param name="ct">Токен отмены (проверяется между записями).</param>
    public MftScanResult TryScanDrive(string driveRoot, CancellationToken ct = default)
    {
        // skeleton (День 9, TDD RED) — реализация в feat-коммите
        return new MftScanResult
        {
            Success = false,
            FailureKind = MftScanFailureKind.VolumeError,
            FailureText = null
        };
    }

    /// <summary>
    /// Шов открытия ридера тома (тесты подменяют): null — том принципиально
    /// не открылся (не NTFS/нет диска); ридер с CanRead = false — отказ прав
    /// или структурная проблема (классификация по <see cref="IMftVolumeReader.FailureKind"/>).
    /// </summary>
    /// <param name="letter">Буква тома (A–Z, верхний регистр).</param>
    protected virtual IMftVolumeReader? OpenVolumeReader(char letter)
    {
        return MftVolume.TryOpenVolume(letter, out IMftVolumeReader? reader, out _) ? reader : null;
    }

    /// <summary>
    /// Шов дешёвой проверки NTFS без открытия тома (DriveInfo.DriveFormat,
    /// прав не требует) — префектор режима Auto до попытки чтения $MFT.
    /// </summary>
    /// <param name="letter">Буква тома (A–Z, верхний регистр).</param>
    protected virtual bool IsNtfsVolume(char letter)
    {
        try
        {
            var drive = new DriveInfo(letter.ToString());
            return drive.IsReady && string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Конверсия дерева в топ-N больших файлов (как LargeFilesEngine):
    /// путь = префикс поддерева + имена; размер = итоговый размер узла
    /// (Allocated &gt; 0 ? Allocated : Real); Modified — default (движок
    /// обогащает best-effort только для финальных top-N).
    /// </summary>
    /// <param name="subtree">Поддерево (обычно корень тома или найданный по пути узел).</param>
    /// <param name="subtreeRootPath">Путь поддерева (префикс путей файлов).</param>
    /// <param name="minSize">Минимальный размер файла в байтах.</param>
    /// <param name="maxCount">Максимум возвращаемых файлов (Top-N).</param>
    public static List<LargeFileInfo> ToLargeFiles(
        MftTreeNode subtree,
        string subtreeRootPath,
        long minSize = 1024 * 1024,
        int maxCount = 50)
    {
        // skeleton (День 9, TDD RED) — реализация в feat-коммите
        return [];
    }

    /// <summary>
    /// Конверсия дерева в папки ≥ порога — «как DiskUsageScanner выдаёт»:
    /// ScannedItem c Risk/Description по той же логике; корень поддерева
    /// сам не выдаётся (зеркало walk-режима: depth &gt; 0).
    /// </summary>
    /// <param name="subtree">Поддерево (обычно узел, найданный по пути сканирования).</param>
    /// <param name="subtreeRootPath">Путь поддерева (префикс путей + ParentApp-корень).</param>
    /// <param name="minBytes">Минимальный итоговый размер папки в байтах.</param>
    public static List<ScannedItem> ToFolderItems(
        MftTreeNode subtree,
        string subtreeRootPath,
        long minBytes)
    {
        // skeleton (День 9, TDD RED) — реализация в feat-коммите
        return [];
    }

    /// <summary>
    /// Конверсия дерева в узлы DiskNode для DiskMapEngine (зеркало
    /// BuildNode/CalculatePercentages: файлы на каждом уровне, спуск в
    /// подпапки до maxDepth, сортировка детей по размеру desc, проценты).
    /// </summary>
    /// <param name="subtree">Поддерево (корень тома или узел по пути).</param>
    /// <param name="rootPath">Путь поддерева (FullPath корневого узла).</param>
    /// <param name="maxDepth">Максимум глубины (0 = без ограничений).</param>
    public static DiskNode ToTreemapInput(
        MftTreeNode subtree,
        string rootPath,
        int maxDepth = 0)
    {
        // skeleton (День 9, TDD RED) — реализация в feat-коммите
        return new DiskNode();
    }

    /// <summary>
    /// Ищет узел поддерева по абсолютному пути (навигация по детям
    /// OrdinalIgnoreCase, сегменты — только каталоги). null — пути нет
    /// в дереве (в т.ч. вне тома).
    /// </summary>
    /// <param name="tree">Дерево тома.</param>
    /// <param name="volumeRoot">Корень тома ("C:\").</param>
    /// <param name="absolutePath">Абсолютный путь на томе.</param>
    public static MftTreeNode? FindSubtree(MftTree tree, string volumeRoot, string absolutePath)
    {
        // skeleton (День 9, TDD RED) — реализация в feat-коммите
        return null;
    }
}
