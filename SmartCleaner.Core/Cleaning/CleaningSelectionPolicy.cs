using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

/// <summary>
/// Политика выбора элементов к очистке (ROADMAP v3.1, День 14, фаза B).
/// Чистая Core-логика без UI: как VM решает, что вообще можно удалить и нужен ли
/// дополнительный диалог подтверждения. Вынесена из VM, чтобы инварианты
/// безопасности проверялись тестами без WPF.
/// </summary>
public static class CleaningSelectionPolicy
{
    /// <summary>
    /// Элемент допустим к очистке: обычные элементы — как раньше;
    /// <see cref="RiskCategory.UserData"/> — только при явном opt-in
    /// (<see cref="ScannedItem.IsUserDataOptIn"/>).
    /// </summary>
    public static bool IsEligible(ScannedItem item) => item.Risk != RiskCategory.UserData;

    /// <summary>
    /// В выборе есть opt-in пользовательские данные (cookie-хранилища):
    /// перед очисткой требуется дополнительное подтверждение «будете разлогинены».
    /// </summary>
    public static bool RequiresUserDataConfirmation(IEnumerable<ScannedItem> selected) => false;
}