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
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> ≤ 0.</exception>
    public static bool ShouldCheck(DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval), interval, "Интервал проверки должен быть положительным.");
        }

        if (lastCheckUtc is null)
        {
            return true;
        }

        var elapsed = nowUtc - lastCheckUtc.Value;
        if (elapsed < TimeSpan.Zero)
        {
            // Отметка «из будущего»: системные часы откатили — состоянию не верим,
            // но и не блокируем проверку навсегда (перезапись отметки лечит состояние)
            return true;
        }

        return elapsed >= interval;
    }

    /// <summary>
    /// Сколько ждать до следующей проверки: при due — <paramref name="firstCheckDelay"/>,
    /// иначе — остаток интервала (перезапуски не сбрасывают суточный ритм).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> ≤ 0 или <paramref name="firstCheckDelay"/> &lt; 0.
    /// </exception>
    public static TimeSpan InitialDelay(
        DateTimeOffset? lastCheckUtc, DateTimeOffset nowUtc, TimeSpan interval, TimeSpan firstCheckDelay)
    {
        if (firstCheckDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstCheckDelay), firstCheckDelay, "Задержка первой проверки не может быть отрицательной.");
        }

        if (ShouldCheck(lastCheckUtc, nowUtc, interval))
        {
            return firstCheckDelay;
        }

        var remaining = interval - (nowUtc - lastCheckUtc!.Value);
        return remaining > TimeSpan.Zero ? remaining : firstCheckDelay;
    }
}