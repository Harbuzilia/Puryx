using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

/// <summary>
/// Политика выбора элементов к очистке (ROADMAP v3.1, День 14, фаза B):
/// чистая Core-логика без UI — что вообще можно удалить и нужен ли
/// дополнительный диалог подтверждения. Вынесена из VM, чтобы инварианты
/// безопасности (cookie-opt-in, «защищённое не удаляется») проверялись тестами
/// без WPF. UI обязан спрашивать политику, а не дублировать условия.
/// </summary>
public static class CleaningSelectionPolicy
{
    /// <summary>
    /// Элемент допустим к очистке: обычные элементы — как раньше;
    /// <see cref="RiskCategory.UserData"/> — только при явном opt-in
    /// (<see cref="ScannedItem.IsUserDataOptIn"/>). Автовыбор opt-in-элементов
    /// не делает никто: сканер выдаёт их с <c>IsSelected = false</c>.
    /// </summary>
    public static bool IsEligible(ScannedItem item) =>
        item.Risk != RiskCategory.UserData || item.IsUserDataOptIn;

    /// <summary>
    /// В выборе есть opt-in пользовательские данные (cookie-хранилища браузеров):
    /// перед очисткой требуется дополнительное подтверждение
    /// «будете разлогинены». Принимает именно выбранные элементы.
    /// </summary>
    public static bool RequiresUserDataConfirmation(IEnumerable<ScannedItem> selected) =>
        selected.Any(item => item.IsUserDataOptIn);
}