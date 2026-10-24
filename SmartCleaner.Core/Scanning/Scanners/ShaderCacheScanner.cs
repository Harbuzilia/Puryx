using Microsoft.Extensions.Logging;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Localization;
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

        progress?.Report(Localizer.GetString("Scanner_Shader_Progress_Searching"));

        // 1. DirectX D3DS Shader Cache
        var d3dsCache = Path.Combine(localAppData, "D3DSCache");
        AddShaderDirectory(items, d3dsCache, "DirectX Shader Cache", "Scanner_Shader_Desc_DirectX", RiskCategory.SafeToDelete);

        // 2. NVIDIA Shader Caches
        var nvDxCache = Path.Combine(localAppData, "NVIDIA", "DXCache");
        AddShaderDirectory(items, nvDxCache, "NVIDIA DirectX Shader Cache (DXCache)", "Scanner_Shader_Desc_NvDx", RiskCategory.SafeToDelete);

        var nvGlCache = Path.Combine(localAppData, "NVIDIA", "GLCache");
        AddShaderDirectory(items, nvGlCache, "NVIDIA OpenGL/Vulkan Cache (GLCache)", "Scanner_Shader_Desc_NvGl", RiskCategory.SafeToDelete);

        var nvComputeCache = Path.Combine(localAppData, "NVIDIA Corporation", "NV_Cache");
        AddShaderDirectory(items, nvComputeCache, "NVIDIA Compute Cache", "Scanner_Shader_Desc_NvCompute", RiskCategory.SafeToDelete);

        // 3. AMD Shader Caches
        var amdDxCache = Path.Combine(localAppData, "AMD", "DxCache");
        AddShaderDirectory(items, amdDxCache, "AMD Radeon Shader Cache (DxCache)", "Scanner_Shader_Desc_AmdDx", RiskCategory.SafeToDelete);

        var amdGlCache = Path.Combine(localAppData, "AMD", "GLCache");
        AddShaderDirectory(items, amdGlCache, "AMD OpenGL/Vulkan Shader Cache", "Scanner_Shader_Desc_AmdGl", RiskCategory.SafeToDelete);

        // 4. Intel Shader Cache
        var intelCache = Path.Combine(localAppData, "Intel", "ShaderCache");
        AddShaderDirectory(items, intelCache, "Intel Graphics Shader Cache", "Scanner_Shader_Desc_Intel", RiskCategory.SafeToDelete);

        // 5. Steam Shader Pre-Caching
        var steamShaderCache = Path.Combine(programFilesX86, "Steam", "steamapps", "shadercache");
        AddShaderDirectory(items, steamShaderCache, "Steam Shader Pre-Caching", "Scanner_Shader_Desc_Steam", RiskCategory.PerformanceCache);

        var result = new ScanResult
        {
            CategoryName = CategoryName,
            CategoryIcon = CategoryIcon,
            Items = items
        };

        return Task.FromResult(result);
    }

    private void AddShaderDirectory(List<ScannedItem> items, string path, string name, string descKey, RiskCategory risk)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;

        try
        {
            var size = CalculateDirectorySize(path);
            if (size > 0)
            {
                var desc = Localizer.GetString(descKey);
                items.Add(new ScannedItem
                {
                    Path = path,
                    Size = size,
                    LastAccess = Directory.GetLastWriteTime(path),
                    Description = $"{name}: {desc}",
                    Explanation = desc,
                    HowToRestore = Localizer.GetString("Scanner_Shader_HowTo_Default"),
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
