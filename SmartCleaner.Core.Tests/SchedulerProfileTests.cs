using SmartCleaner.Core.Scheduler;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты маппинга имени профиля (--profile из CLI/планировщика) в набор
/// включаемых категорий сканеров (CleaningProfileMap).
/// Наборы категорий 1-в-1 с MainViewModel.ApplyProfile (UI-семантика).
/// </summary>
public class SchedulerProfileTests
{
    // Все категории сканеров приложения (как их строит MainViewModel в ScannerOptions)
    private static readonly string[] AllCategories =
    [
        "Система", "Браузеры", "Приложения", "Игры",
        "Кэши пакетов", "Node Modules", "NPM Packages", "Python Packages",
        "Python Venv", "Сборка .NET", "Docker", "AI-агенты",
        "Анализ диска", "Кэши шейдеров GPU и DirectX",
        "Разработка (Dev Super-Cleaner)", "Выбранная папка"
    ];

    /// <summary>
    /// Категории, которые профиль оставляет включёнными при выборе из полного списка
    /// (механика = MainViewModel.ApplyProfile / авто-очистка из CLI).
    /// </summary>
    private static List<string> SelectEnabled(string profile)
    {
        CleaningProfileMap.TryGetEnabledCategories(profile, out var enabled);
        return AllCategories.Where(c => enabled == null || enabled.Contains(c)).ToList();
    }

    [Fact]
    public void KnownProfiles_AreTheThreeSchedulerProfiles()
    {
        Assert.Equal(new[] { "Быстрая", "Разработка", "Полное" }, CleaningProfileMap.KnownProfiles);
    }

    [Fact]
    public void QuickProfile_EnablesBaseScannerCategories()
    {
        Assert.True(CleaningProfileMap.TryGetEnabledCategories("Быстрая", out var enabled));
        Assert.NotNull(enabled);

        Assert.Equal(new[] { "Система", "Браузеры", "Приложения" }, SelectEnabled("Быстрая"));
    }

    [Fact]
    public void DevelopmentProfile_SetMatchesUiSemantics()
    {
        Assert.True(CleaningProfileMap.TryGetEnabledCategories("Разработка", out var enabled));
        Assert.NotNull(enabled);

        var expected = new HashSet<string>
        {
            "Кэши пакетов", "Node Modules", "npm пакеты", "Python пакеты",
            "Python окружения", ".NET Артефакты", "Docker", "AI Агенты"
        };
        Assert.True(expected.SetEquals(enabled));
    }

    [Fact]
    public void DevelopmentProfile_SelectsDevScannersFromFullUniverse()
    {
        // Эффективный набор из полного списка категорий: в UI-наборе «Разработка»
        // реально существуют «Кэши пакетов», «Node Modules», «Docker»; остальные
        // имена не совпадают с CategoryName сканеров (дрейф имён в
        // MainViewModel.ApplyProfile — пре-существующий, вне скоупа M3).
        Assert.Equal(new[] { "Кэши пакетов", "Node Modules", "Docker" }, SelectEnabled("Разработка"));
    }

    [Fact]
    public void FullProfile_EnablesAllCategories()
    {
        Assert.True(CleaningProfileMap.TryGetEnabledCategories("Полное", out var enabled));
        Assert.Null(enabled); // null = без ограничений, все категории включены

        Assert.Equal(AllCategories, SelectEnabled("Полное"));
    }

    [Fact]
    public void UnknownProfile_IsRejectedWithNoRestrictions()
    {
        Assert.False(CleaningProfileMap.TryGetEnabledCategories("Максимум", out var enabled));
        Assert.Null(enabled);
    }
}
