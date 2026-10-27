using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Models;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Политика выбора к очистке (ROADMAP v3.1, День 14, фаза B): VM-логика
/// «что можно удалить» и «нужен ли дополнительный диалог» вынесена в Core,
/// чтобы инварианты безопасности проверялись без WPF.
/// <see cref="RiskCategory.UserData"/> по умолчанию недопустим; исключение —
/// только явный opt-in (<see cref="ScannedItem.IsUserDataOptIn"/>).
/// </summary>
public class CleaningSelectionPolicyTests
{
    private static ScannedItem Item(RiskCategory risk, bool optIn = false) => new()
    {
        Path = @"C:\Users\user\AppData\Local\Google\Chrome\User Data\Default\Network\Cookies",
        Risk = risk,
        IsUserDataOptIn = optIn
    };

    [Theory]
    [InlineData(RiskCategory.SafeToDelete)]
    [InlineData(RiskCategory.PerformanceCache)]
    public void IsEligible_RegularItems_Allowed(RiskCategory risk)
    {
        Assert.True(CleaningSelectionPolicy.IsEligible(Item(risk)));
    }

    [Fact]
    public void IsEligible_UserDataWithoutOptIn_Rejected()
    {
        Assert.False(CleaningSelectionPolicy.IsEligible(Item(RiskCategory.UserData)));
    }

    [Fact]
    public void IsEligible_UserDataWithOptIn_Allowed()
    {
        Assert.True(CleaningSelectionPolicy.IsEligible(Item(RiskCategory.UserData, optIn: true)));
    }

    [Fact]
    public void RequiresUserDataConfirmation_TrueOnlyWhenOptInUserDataSelected()
    {
        Assert.False(CleaningSelectionPolicy.RequiresUserDataConfirmation(
            [Item(RiskCategory.SafeToDelete), Item(RiskCategory.PerformanceCache)]));

        Assert.False(CleaningSelectionPolicy.RequiresUserDataConfirmation(
            [Item(RiskCategory.UserData)])); // защищённый элемент и так не удаляется

        Assert.True(CleaningSelectionPolicy.RequiresUserDataConfirmation(
            [Item(RiskCategory.PerformanceCache), Item(RiskCategory.UserData, optIn: true)]));
    }
}