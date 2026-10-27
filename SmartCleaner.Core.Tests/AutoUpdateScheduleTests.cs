using SmartCleaner.Core.Update;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Тесты расписания авто-проверки обновлений (ROADMAP v3.1, День 13, фаза A).
/// Логика чистая: «сейчас» — параметр, часы/сеть не используются. Покрыт
/// контракт «не чаще раза в сутки»: первая проверка, точная граница интервала,
/// ранний старт после перезапуска, отметка из будущего (откат часов),
/// независимость от UTC-смещения отметки и задержка первой проверки.
/// </summary>
public class AutoUpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Day = TimeSpan.FromHours(24);

    [Fact]
    public void ShouldCheck_NoPreviousCheck_ReturnsTrue()
    {
        Assert.True(AutoUpdateSchedule.ShouldCheck(null, Now, Day));
    }

    [Fact]
    public void ShouldCheck_ExactlyOneIntervalElapsed_ReturnsTrue()
    {
        Assert.True(AutoUpdateSchedule.ShouldCheck(Now - Day, Now, Day));
    }

    [Fact]
    public void ShouldCheck_LessThanIntervalElapsed_ReturnsFalse()
    {
        // Перезапуск через час после проверки не должен порождать сетевой запрос
        Assert.False(AutoUpdateSchedule.ShouldCheck(Now - TimeSpan.FromHours(23), Now, Day));
    }

    [Fact]
    public void ShouldCheck_LastCheckInFuture_TreatedAsStaleAndChecks()
    {
        // Часы откатили: отметка недостоверна — одна проверка и перезапись состояния
        Assert.True(AutoUpdateSchedule.ShouldCheck(Now + TimeSpan.FromHours(2), Now, Day));
    }

    [Fact]
    public void ShouldCheck_DifferentUtcOffsets_ComparesInstants()
    {
        // 10:00+03:00 = 07:00Z; сейчас 08:00Z — прошёл час, проверка не нужна
        var last = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.FromHours(3));
        var now = new DateTimeOffset(2026, 10, 27, 8, 0, 0, TimeSpan.Zero);

        Assert.False(AutoUpdateSchedule.ShouldCheck(last, now, Day));
    }

    [Fact]
    public void InitialDelay_WhenDue_ReturnsFirstCheckDelay()
    {
        var delay = AutoUpdateSchedule.InitialDelay(
            lastCheckUtc: null,
            Now,
            Day,
            firstCheckDelay: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromSeconds(30), delay);
    }

    [Fact]
    public void InitialDelay_WhenNotDue_ReturnsRemainingInterval()
    {
        // Проверка была 20 часов назад → ждать 4 часа, а не сутки с момента старта
        var delay = AutoUpdateSchedule.InitialDelay(
            Now - TimeSpan.FromHours(20),
            Now,
            Day,
            firstCheckDelay: TimeSpan.FromSeconds(30));

        Assert.Equal(TimeSpan.FromHours(4), delay);
    }

    [Fact]
    public void ShouldCheck_NonPositiveInterval_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AutoUpdateSchedule.ShouldCheck(null, Now, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AutoUpdateSchedule.ShouldCheck(null, Now, TimeSpan.FromHours(-1)));
    }
}