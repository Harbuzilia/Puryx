namespace SmartCleaner.Core.Update;

/// <summary>
/// Расписание фоновой авто-проверки обновлений (ROADMAP v3.1, День 13, фаза A):
/// чистая логика «не чаще раза в сутки», без таймеров и сети. UI-слой
/// (<c>TrayService</c>) только спрашивает, можно ли проверять сейчас и сколько
/// ждать до следующей проверки; время передаётся параметром — тесты без часов.
/// </summary>
public static class AutoUpdateSchedule
{
    /// <summary>Интервал между авто-проверками: сутки.</summary>
    public static TimeSpan DefaultInterval => TimeSpan.FromHours(24);

    /// <summary>Задержка первой проверки после старта приложения (не бьёт по запуску).</summary>
    public static TimeSpan FirstCheckDelay => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Пора ли проверять обновления. Отметка в будущем (часы откатили) считается
    /// недостоверной — одна проверка разрешается и перезаписывает состояние.
    /// </summary>
    public static bool ShouldCheck(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, TimeSpan interval)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Сколько ждать до следующей проверки: при due — <paramref name="firstCheckDelay"/>,
    /// иначе — остаток интервала (перезапуски не сбрасывают суточный ритм).
    /// </summary>
    public static TimeSpan InitialDelay(
        DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, TimeSpan interval, TimeSpan firstCheckDelay)
    {
        throw new NotImplementedException();
    }
}