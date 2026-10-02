using Microsoft.Extensions.Logging;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;
using System.IO;

namespace SmartCleaner.Core.Scanning.Scanners;

/// <summary>
/// God-Tier: Сканер кэшей шейдеров видеокарт и DirectX (NVIDIA, AMD, Intel, DirectX D3DSCache)
/// </summary>
public class ShaderCacheScanner : ScannerBase
{
    public override string CategoryName => "Кэши шейдеров GPU и DirectX";
    public override string CategoryIcon => "\uE790"; // Graphics/Media icon
    public override int DisplayOrder => 6;

    public ShaderCacheScanner(ISafetyService safety, ILogger<ShaderCacheScanner>? logger = null)
        : base(safety, logger)
    {
    }

    public override Task<ScanResult> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<ScannedItem>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        progress?.Report("Поиск кэшей шейдеров видеокарт и DirectX...");

        // 1. DirectX D3DS Shader Cache
        var d3dsCache = Path.Combine(localAppData, "D3DSCache");
        AddShaderDirectory(items, d3dsCache, "DirectX Shader Cache", "Системный кэш скомпилированных шейдеров DirectX 11/12", RiskCategory.SafeToDelete);

        // 2. NVIDIA Shader Caches
        var nvDxCache = Path.Combine(localAppData, "NVIDIA", "DXCache");
        AddShaderDirectory(items, nvDxCache, "NVIDIA DirectX Shader Cache (DXCache)", "Скомпилированные шейдеры драйвера NVIDIA для DirectX игр", RiskCategory.SafeToDelete);

        var nvGlCache = Path.Combine(localAppData, "NVIDIA", "GLCache");
        AddShaderDirectory(items, nvGlCache, "NVIDIA OpenGL/Vulkan Cache (GLCache)", "Скомпилированные шейдеры OpenGL и Vulkan драйвера NVIDIA", RiskCategory.SafeToDelete);

        var nvComputeCache = Path.Combine(localAppData, "NVIDIA Corporation", "NV_Cache");
        AddShaderDirectory(items, nvComputeCache, "NVIDIA Compute Cache", "Временный кэш вычислений NVIDIA CUDA/DirectX", RiskCategory.SafeToDelete);

        // 3. AMD Shader Caches
        var amdDxCache = Path.Combine(localAppData, "AMD", "DxCache");
        AddShaderDirectory(items, amdDxCache, "AMD Radeon Shader Cache (DxCache)", "Кэш шейдеров видеокарт AMD Radeon", RiskCategory.SafeToDelete);

        var amdGlCache = Path.Combine(localAppData, "AMD", "GLCache");
        AddShaderDirectory(items, amdGlCache, "AMD OpenGL/Vulkan Shader Cache", "Кэш шейдеров Vulkan/OpenGL AMD", RiskCategory.SafeToDelete);

        // 4. Intel Shader Cache
        var intelCache = Path.Combine(localAppData, "Intel", "ShaderCache");
        AddShaderDirectory(items, intelCache, "Intel Graphics Shader Cache", "Кэш шейдеров интегрированной графики Intel", RiskCategory.SafeToDelete);

        // 5. Steam Shader Pre-Caching
        var steamShaderCache = Path.Combine(programFilesX86, "Steam", "steamapps", "shadercache");
        AddShaderDirectory(items, steamShaderCache, "Steam Shader Pre-Caching", "Предварительно скомпилированные шейдеры игр в Steam", RiskCategory.PerformanceCache);

        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };

        return Task.FromResult(result);
    }

    private void AddShaderDirectory(List<ScannedItem> items, string path, string name, string desc, RiskCategory risk)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        try
        {
            var size = CalculateDirectorySize(path);
            if (size > 0)
            {
                items.Add(new ScannedItem
                {
                    Path = path,
                    Size = size,
                    LastAccess = Directory.GetLastWriteTime(path),
                    Description = $"{name}: {desc}",
                    Explanation = desc,
                    HowToRestore = "Шейдеры автоматически перекомпилируются графическим драйвером при следующем запуске игры.",
                    Risk = risk,
                    IsDirectory = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка при сканировании шейдеров в {Path}", path);
        }
    }
}
