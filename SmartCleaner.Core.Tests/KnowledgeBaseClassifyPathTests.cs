using SmartCleaner.Core.Knowledge;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 16б: граница каталога в KnowledgeBase.ClassifyPath. Корень приложения
/// обязан совпадать с путём или быть его родительским каталогом:
/// «%APPDATA%\Claude» не матчит «...\ClaudeFoo» — иначе sibling-префикс
/// получал паттерны (и защиту) чужого приложения — over-blocking.
/// </summary>
public class KnowledgeBaseClassifyPathTests
{
    private readonly KnowledgeBase _knowledge = new(new InMemoryConfigService([]));

    private static string AppData(string relative) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), relative);

    private static string UserProfile(string relative) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), relative);

    [Fact]
    public void ClassifyPath_SiblingPrefixOfAppRoot_IsNotKnownApp()
    {
        // Находка ревью: %APPDATA%\Claude матчит ...\ClaudeFoo — over-blocking
        // (путь ClaudeFoo\random.db блокировался защищённым паттерном *.db приложения Claude).
        var path = AppData(Path.Combine("ClaudeFoo", "random.db"));

        var classification = _knowledge.ClassifyPath(path);

        Assert.False(classification.IsKnownApp,
            $"sibling-каталог «{path}» не относится к приложению Claude Desktop, " +
            "но классифицирован как известное приложение (нет границы каталога)");
    }

    [Fact]
    public void ClassifyPath_SiblingOfDotPrefixedRoot_IsNotKnownApp()
    {
        // .gemini — полностью защищённое приложение (ProtectedPatterns = ["**"]):
        // без границы каталога sibling «.gemini2» целиком наследовал бы его защиту.
        var path = UserProfile(Path.Combine(".gemini2", "file.txt"));

        var classification = _knowledge.ClassifyPath(path);

        Assert.False(classification.IsKnownApp,
            $"sibling-каталог «{path}» не относится к приложению Antigravity, " +
            "но классифицирован как известное приложение (нет границы каталога)");
    }

    [Fact]
    public void ClassifyPath_AppRootDirectoryItself_IsKnownApp()
    {
        var path = AppData("Claude");

        var classification = _knowledge.ClassifyPath(path);

        Assert.True(classification.IsKnownApp, "сам корень приложения — известное приложение");
    }

    [Fact]
    public void ClassifyPath_ProtectedPatternInsideAppRoot_StillProtected()
    {
        var path = AppData(Path.Combine("Claude", "random.db"));

        var classification = _knowledge.ClassifyPath(path);

        Assert.True(classification.IsKnownApp);
        Assert.Equal(RiskCategory.UserData, classification.SuggestedRisk);
    }

    [Fact]
    public void ClassifyPath_CachePatternInsideAppRoot_StillCache()
    {
        var path = AppData(Path.Combine("Cursor", "Cache"));

        var classification = _knowledge.ClassifyPath(path);

        Assert.True(classification.IsKnownApp);
        Assert.Equal(RiskCategory.PerformanceCache, classification.SuggestedRisk);
    }
}
