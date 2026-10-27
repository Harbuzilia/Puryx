using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// Сканер системных временных файлов
/// </summary>
public class SystemScanner : ScannerBase
{
    public override string CategoryName => "Система";
    public override string CategoryIcon => "\uE770"; // Segoe MDL2: Settings
    public override int DisplayOrder => 1;

    private readonly IConfigService? _configService;
    private readonly IReadOnlyList<string>? _deepScanRoots;

    /// <summary>
    /// DI-конструктор. <paramref name="configService"/> — единая настройка
    /// сканирования (<see cref="ScanSettingsConfig"/>; deep-pass opt-in), null —
    /// дефолты (проход выключен). <paramref name="deepScanRoots"/> — тест-шов
    /// корней deep-pass (null — %TEMP%, Windows\Temp, WER/Minidump, FOUND.*).
    /// </summary>
    public SystemScanner(
        ISafetyService safety,
        IConfigService? configService = null,
        IReadOnlyList<string>? deepScanRoots = null)
        : base(safety)
    {
        _configService = configService;
        _deepScanRoots = deepScanRoots;
    }

    public override async Task<ScanResult> ScanAsync(
        IProgress<string>? progress = null, 
        CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();

        await Task.Run(() =>
        {
            // 1. Пользовательская папка Temp
            progress?.Report(Localizer.GetString("Scanner_System_Progress_UserTemp"));
            ScanUserTemp(items, ct);

            // 2. Системная папка Temp (если есть доступ)
            progress?.Report(Localizer.GetString("Scanner_System_Progress_SystemTemp"));
            ScanSystemTemp(items, ct);

            // 3. Prefetch
            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Prefetch"));
            ScanPrefetch(items, ct);

            // 4. Windows Update Downloads
            progress?.Report(Localizer.GetString("Scanner_System_Progress_UpdateCache"));
            ScanWindowsUpdateCache(items, ct);

            // 5. Crash Dumps (приложений — не путать с BSOD дампами в пункте 11)
            progress?.Report(Localizer.GetString("Scanner_System_Progress_CrashDumps"));
            ScanCrashDumps(items, ct);

            // 7. Windows Error Reports
            progress?.Report(Localizer.GetString("Scanner_System_Progress_ErrorReports"));
            ScanErrorReports(items, ct);

            // 8. Delivery Optimization
            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Delivery Optimization"));
            ScanDeliveryOptimization(items, ct);

            // 9. DirectX Shader Cache
            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "DirectX Shader Cache"));
            ScanDirectXShaderCache(items, ct);

            // 10. Windows.old
            progress?.Report(Localizer.GetString("Scanner_Progress_Scanning", "Windows.old"));
            ScanWindowsOld(items, ct);

            // 11. Memory Dumps (большие дампы памяти)
            progress?.Report(Localizer.GetString("Scanner_System_Progress_MemoryDumps"));
            ScanMemoryDumps(items, ct);

            // 12. Windows Installer Temp
            progress?.Report(Localizer.GetString("Scanner_System_Progress_InstallerCache"));
            ScanInstallerCache(items, ct);

            // 13. Font Cache
            progress?.Report(Localizer.GetString("Scanner_System_Progress_FontCache"));
            ScanFontCache(items, ct);

            // 14. Кэш DNS (FlushDNS)
            progress?.Report(Localizer.GetString("Scanner_System_Progress_IconCache"));
            ScanIconCache(items, ct);

        }, ct);

        return new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };
    }

    private void ScanUserTemp(List<ScannedItem> items, CancellationToken ct)
    {
        var tempPath = Path.GetTempPath();
        if (!Directory.Exists(tempPath)) return;

        try
        {
            // Сканируем файлы с определёнными расширениями
            var tempExtensions = new[] { ".tmp", ".log", ".bak", ".old", ".temp" };
            
            foreach (var file in Directory.EnumerateFiles(tempPath, "*", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (tempExtensions.Contains(ext) || Path.GetFileName(file).StartsWith("~"))
                {
                    if (!_safety.IsWithinProtectedPeriod(file) && !_safety.IsWhitelisted(file))
                    {
                        items.Add(CreateFileItem(file, RiskCategory.SafeToDelete, Localizer.GetString("Scanner_Desc_TempFile")));
                    }
                }
            }

            // Сканируем подпапки
            foreach (var dir in Directory.EnumerateDirectories(tempPath))
            {
                ct.ThrowIfCancellationRequested();
                
                var dirName = Path.GetFileName(dir).ToLowerInvariant();
                
                // Пропускаем некоторые системные папки
                if (dirName.StartsWith(".") || dirName == "low")
                    continue;

                if (!_safety.IsWithinProtectedPeriod(dir) && !_safety.IsWhitelisted(dir))
                {
                    items.Add(CreateDirectoryItem(dir, RiskCategory.SafeToDelete, Localizer.GetString("Scanner_Desc_TempFolder")));
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanSystemTemp(List<ScannedItem> items, CancellationToken ct)
    {
        var systemTemp = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), 
            "Temp");
        
        if (!Directory.Exists(systemTemp)) return;

        try
        {
            var size = CalculateDirectorySize(systemTemp);
            if (size > 0)
            {
                items.Add(new ScannedItem
                {
                    Path = systemTemp,
                    Size = size,
                    LastAccess = GetLastAccess(systemTemp),
                    Risk = RiskCategory.SafeToDelete,
                    Description = Localizer.GetString("Scanner_System_Desc_SystemTemp"),
                    IsDirectory = true,
                    IsSelected = false // Не выбираем по умолчанию — требует elevation
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanPrefetch(List<ScannedItem> items, CancellationToken ct)
    {
        var prefetchPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Prefetch");

        if (!Directory.Exists(prefetchPath)) return;

        try
        {
            var size = CalculateDirectorySize(prefetchPath);
            if (size > 0)
            {
                items.Add(new ScannedItem
                {
                    Path = prefetchPath,
                    Size = size,
                    LastAccess = GetLastAccess(prefetchPath),
                    Risk = RiskCategory.PerformanceCache,
                    Description = Localizer.GetString("Scanner_System_Desc_Prefetch"),
                    IsDirectory = true,
                    IsSelected = false
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanWindowsUpdateCache(List<ScannedItem> items, CancellationToken ct)
    {
        var updatePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SoftwareDistribution", "Download");

        if (!Directory.Exists(updatePath)) return;

        try
        {
            var size = CalculateDirectorySize(updatePath);
            if (size > 1024 * 1024) // > 1 MB
            {
                items.Add(new ScannedItem
                {
                    Path = updatePath,
                    Size = size,
                    LastAccess = GetLastAccess(updatePath),
                    Risk = RiskCategory.SafeToDelete,
                    Description = Localizer.GetString("Scanner_System_Desc_UpdateCache"),
                    IsDirectory = true,
                    IsSelected = false
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    // ScanThumbnailCache удалён — заменён на ScanIconCache, который покрывает thumbcache + iconcache

    /// <summary>
    /// Дампы приложений (AppCrash) — НЕ BSOD.
    /// BSOD дампы — в ScanMemoryDumps.
    /// </summary>
    private void ScanCrashDumps(List<ScannedItem> items, CancellationToken ct)
    {
        var dumpPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrashDumps");

        if (!Directory.Exists(dumpPath)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(dumpPath, "*.dmp"))
            {
                ct.ThrowIfCancellationRequested();
                items.Add(CreateFileItem(file, RiskCategory.SafeToDelete, Localizer.GetString("Scanner_System_Desc_CrashDump")));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private void ScanErrorReports(List<ScannedItem> items, CancellationToken ct)
    {
        var werPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "WER");

        if (!Directory.Exists(werPath)) return;

        try
        {
            var size = CalculateDirectorySize(werPath);
            if (size > 1024 * 1024) // > 1 MB
            {
                items.Add(CreateDirectoryItem(werPath, RiskCategory.SafeToDelete, Localizer.GetString("Scanner_System_Desc_ErrorReports")));
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Delivery Optimization — кэш P2P-обновлений Windows.
    /// Может занимать гигабайты на SSD.
    /// </summary>
    private void ScanDeliveryOptimization(List<ScannedItem> items, CancellationToken ct)
    {
        var paths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), 
                "SoftwareDistribution", "DeliveryOptimization"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "SoftwareDistribution", "Download")
        };

        foreach (var path in paths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                ct.ThrowIfCancellationRequested();
                var size = CalculateDirectorySize(path);
                if (size > 10 * 1024 * 1024) // > 10 MB
                {
                    items.Add(new ScannedItem
                    {
                        Path = path,
                        Size = size,
                        LastAccess = GetLastAccess(path),
                        Risk = RiskCategory.SafeToDelete,
                        Description = Localizer.GetString("Scanner_System_Desc_DeliveryOptimization"),
                        Explanation = Localizer.GetString("Scanner_System_Expl_DeliveryOptimization"),
                        HowToRestore = Localizer.GetString("Scanner_System_HowTo_DeliveryOptimization"),
                        IsDirectory = true,
                        IsSelected = true
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// DirectX Shader Cache — предкомпилированные шейдеры.
    /// Безопасно для удаления, перекомпилируются при запуске игр/приложений.
    /// </summary>
    private void ScanDirectXShaderCache(List<ScannedItem> items, CancellationToken ct)
    {
        var d3dCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "D3DSCache");

        if (!Directory.Exists(d3dCache)) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var size = CalculateDirectorySize(d3dCache);
            if (size > 1024 * 1024)
            {
                items.Add(new ScannedItem
                {
                    Path = d3dCache,
                    Size = size,
                    LastAccess = GetLastAccess(d3dCache),
                    Risk = RiskCategory.SafeToDelete,
                    Description = Localizer.GetString("Scanner_System_Desc_ShaderCache"),
                    Explanation = Localizer.GetString("Scanner_System_Expl_ShaderCache"),
                    HowToRestore = Localizer.GetString("Scanner_System_HowTo_ShaderCache"),
                    IsDirectory = true,
                    IsSelected = true
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Windows.old — предыдущая установка Windows.
    /// Может занимать 10-30 ГБ. Появляется после обновления Windows.
    /// </summary>
    private void ScanWindowsOld(List<ScannedItem> items, CancellationToken ct)
    {
        var windowsOld = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Windows.old");
        if (!Directory.Exists(windowsOld)) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var size = CalculateDirectorySize(windowsOld);
            if (size > 0)
            {
                items.Add(new ScannedItem
                {
                    Path = windowsOld,
                    Size = size,
                    LastAccess = GetLastAccess(windowsOld),
                    Risk = RiskCategory.PerformanceCache, // НЕ SafeToDelete — нужно подтверждение
                    Description = Localizer.GetString("Scanner_System_Desc_WindowsOld"),
                    Explanation = Localizer.GetString("Scanner_System_Expl_WindowsOld"),
                    HowToRestore = Localizer.GetString("Scanner_System_HowTo_WindowsOld"),
                    IsDirectory = true,
                    IsSelected = false // Не выбирать автоматически!
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Memory Dumps — полные дампы памяти при BSOD.
    /// MEMORY.DMP может быть 4-16 ГБ.
    /// </summary>
    private void ScanMemoryDumps(List<ScannedItem> items, CancellationToken ct)
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        // Полный дамп памяти
        var fullDump = Path.Combine(winDir, "MEMORY.DMP");
        if (File.Exists(fullDump))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                items.Add(new ScannedItem
                {
                    Path = fullDump,
                    Size = GetFileSize(fullDump),
                    LastAccess = GetLastAccess(fullDump),
                    Risk = RiskCategory.SafeToDelete,
                    Description = Localizer.GetString("Scanner_System_Desc_MemoryDump"),
                    Explanation = Localizer.GetString("Scanner_System_Expl_MemoryDump"),
                    HowToRestore = Localizer.GetString("Scanner_System_HowTo_MemoryDump"),
                    IsDirectory = false,
                    IsSelected = true
                });
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SystemScanner] Memory dump add error: {ex.Message}"); }
        }

        // Мини-дампы
        var miniDumpDir = Path.Combine(winDir, "Minidump");
        if (Directory.Exists(miniDumpDir))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var size = CalculateDirectorySize(miniDumpDir);
                if (size > 0)
                {
                    items.Add(new ScannedItem
                    {
                        Path = miniDumpDir,
                        Size = size,
                        LastAccess = GetLastAccess(miniDumpDir),
                        Risk = RiskCategory.SafeToDelete,
                        Description = Localizer.GetString("Scanner_System_Desc_Minidumps"),
                        Explanation = Localizer.GetString("Scanner_System_Expl_Minidumps"),
                        HowToRestore = Localizer.GetString("Scanner_System_HowTo_Minidumps"),
                        IsDirectory = true,
                        IsSelected = true
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Windows Installer Cache — $PatchCache$ и временные файлы установщика.
    /// </summary>
    private void ScanInstallerCache(List<ScannedItem> items, CancellationToken ct)
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var patchCache = Path.Combine(winDir, "Installer", "$PatchCache$");

        if (Directory.Exists(patchCache))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var size = CalculateDirectorySize(patchCache);
                if (size > 50 * 1024 * 1024) // > 50 MB
                {
                    items.Add(new ScannedItem
                    {
                        Path = patchCache,
                        Size = size,
                        LastAccess = GetLastAccess(patchCache),
                        Risk = RiskCategory.PerformanceCache,
                        Description = Localizer.GetString("Scanner_System_Desc_PatchCache"),
                        Explanation = Localizer.GetString("Scanner_System_Expl_PatchCache"),
                        HowToRestore = Localizer.GetString("Scanner_System_HowTo_PatchCache"),
                        IsDirectory = true,
                        IsSelected = false
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Font Cache — кэш шрифтов Windows.
    /// </summary>
    private void ScanFontCache(List<ScannedItem> items, CancellationToken ct)
    {
        var fontCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "FontCache");

        if (!Directory.Exists(fontCache)) return;

        try
        {
            ct.ThrowIfCancellationRequested();
            var size = CalculateDirectorySize(fontCache);
            if (size > 1024 * 1024) // > 1 MB
            {
                items.Add(new ScannedItem
                {
                    Path = fontCache,
                    Size = size,
                    LastAccess = GetLastAccess(fontCache),
                    Risk = RiskCategory.SafeToDelete,
                    Description = Localizer.GetString("Scanner_System_Desc_FontCache"),
                    Explanation = Localizer.GetString("Scanner_System_Expl_FontCache"),
                    HowToRestore = Localizer.GetString("Scanner_System_HowTo_FontCache"),
                    IsDirectory = true,
                    IsSelected = true
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// Кэш иконок — IconCache.db и thumbcache файлы в Explorer.
    /// </summary>
    private void ScanIconCache(List<ScannedItem> items, CancellationToken ct)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // IconCache.db
        var iconCache = Path.Combine(localAppData, "IconCache.db");
        if (File.Exists(iconCache))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var size = GetFileSize(iconCache);
                if (size > 1024 * 1024)
                {
                    items.Add(new ScannedItem
                    {
                        Path = iconCache,
                        Size = size,
                        LastAccess = GetLastAccess(iconCache),
                        Risk = RiskCategory.SafeToDelete,
                        Description = Localizer.GetString("Scanner_System_Desc_IconCache"),
                        Explanation = Localizer.GetString("Scanner_System_Expl_IconCache"),
                        HowToRestore = Localizer.GetString("Scanner_System_HowTo_IconCache"),
                        IsDirectory = false,
                        IsSelected = true
                    });
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SystemScanner] Cache scan error: {ex.Message}"); }
        }

        // Thumbcache файлы
        var explorerCache = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
        if (Directory.Exists(explorerCache))
        {
            try
            {
                long totalThumb = 0;
                foreach (var file in SafeEnumerateFiles(explorerCache, "thumbcache_*"))
                {
                    ct.ThrowIfCancellationRequested();
                    totalThumb += GetFileSize(file);
                }
                if (totalThumb > 5 * 1024 * 1024  ) // > 5 MB
                {
                    items.Add(new ScannedItem
                    {
                        Path = explorerCache,
                        Size = totalThumb,
                        LastAccess = GetLastAccess(explorerCache),
                        Risk = RiskCategory.SafeToDelete,
                        Description = Localizer.GetString("Scanner_System_Desc_Thumbcache"),
                        Explanation = Localizer.GetString("Scanner_System_Expl_Thumbcache"),
                        HowToRestore = Localizer.GetString("Scanner_System_HowTo_Thumbcache"),
                        IsDirectory = true,
                        IsSelected = true
                    });
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}
