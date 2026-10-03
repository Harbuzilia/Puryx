using SmartCleaner.Core.Stats;
using System.IO;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class CleaningStatsServiceTests
{
    private static string CreateStatsDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smartcleaner-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void RecordSession_InsertsNewestFirstAndAggregates()
    {
        var dir = CreateStatsDir();
        try
        {
            var service = new CleaningStatsService(dir);

            service.RecordSession(100, 5, "Быстрая");
            service.RecordSession(200, 3, "Полное");

            Assert.Equal(2, service.TotalSessions);
            Assert.Equal(200, service.Sessions[0].FreedBytes);
            Assert.Equal("Полное", service.Sessions[0].Profile);
            Assert.Equal(100, service.Sessions[1].FreedBytes);
            Assert.Equal(300, service.TotalFreedBytes);
            Assert.Equal(8, service.TotalFilesDeleted);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sessions_PersistAcrossInstances()
    {
        var dir = CreateStatsDir();
        try
        {
            var first = new CleaningStatsService(dir);
            first.RecordSession(500, 10, "Разработка");

            var second = new CleaningStatsService(dir);

            Assert.Equal(1, second.TotalSessions);
            Assert.Equal(500, second.TotalFreedBytes);
            Assert.Equal(10, second.TotalFilesDeleted);
            Assert.Equal("Разработка", second.Sessions[0].Profile);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Clear_ResetsAllStatistics()
    {
        var dir = CreateStatsDir();
        try
        {
            var service = new CleaningStatsService(dir);
            service.RecordSession(500, 10, "Быстрая");
            service.RecordSession(100, 2, "Полное");

            service.Clear();

            Assert.Equal(0, service.TotalSessions);
            Assert.Equal(0, service.TotalFreedBytes);
            Assert.Empty(service.Sessions);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetMonthlyStats_IncludesTodaysSession()
    {
        var dir = CreateStatsDir();
        try
        {
            var service = new CleaningStatsService(dir);
            service.RecordSession(1000, 4, "Быстрая");

            var stats = service.GetMonthlyStats(1);

            var month = Assert.Single(stats);
            Assert.Equal(1000, month.TotalBytes);
            Assert.Equal(4, month.TotalFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetMonthlyStats_ReturnsRequestedNumberOfMonths()
    {
        var dir = CreateStatsDir();
        try
        {
            var service = new CleaningStatsService(dir);

            var stats = service.GetMonthlyStats(6);

            Assert.Equal(6, stats.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetWeeklyStats_IncludesTodaysSession()
    {
        var dir = CreateStatsDir();
        try
        {
            var service = new CleaningStatsService(dir);
            service.RecordSession(700, 3, "Полное");

            var stats = service.GetWeeklyStats(1);

            Assert.Single(stats);
            Assert.Equal(700, stats[0].TotalBytes);
            Assert.Equal(3, stats[0].TotalFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetWeekStart_ReturnsMondayForEveryDayOfWeek()
    {
        // Неделя: пн 28.09.2026 — вс 04.10.2026
        var monday = new DateTime(2026, 9, 28);
        for (int day = 0; day < 7; day++)
        {
            var date = monday.AddDays(day);

            var weekStart = CleaningStatsService.GetWeekStart(date, 0);

            Assert.Equal(monday, weekStart);
            Assert.Equal(DayOfWeek.Monday, weekStart.DayOfWeek);
        }
    }

    [Fact]
    public void GetWeekStart_StepsBackWholeWeeks()
    {
        // Воскресенье 04.10.2026: текущая неделя началась в пн 28.09
        var sunday = new DateTime(2026, 10, 4);

        Assert.Equal(new DateTime(2026, 9, 28), CleaningStatsService.GetWeekStart(sunday, 0));
        Assert.Equal(new DateTime(2026, 9, 21), CleaningStatsService.GetWeekStart(sunday, 1));
        Assert.Equal(new DateTime(2026, 9, 14), CleaningStatsService.GetWeekStart(sunday, 2));
    }

    [Fact]
    public void GetWeeklyStats_SundaySessionCountedInCurrentWeek()
    {
        var dir = CreateStatsDir();
        try
        {
            // Сессия, записанная в воскресенье 04.10.2026 в 15:00
            var sessionJson = "[{\"Date\":\"2026-10-04T15:00:00\",\"FreedBytes\":700,\"FilesDeleted\":3,\"Profile\":\"Полное\"}]";
            File.WriteAllText(Path.Combine(dir, "cleaning_stats.json"), sessionJson);

            var service = new CleaningStatsService(dir);

            // «Сегодня» — то же воскресенье, вечером после сессии
            var stats = service.GetWeeklyStats(1, new DateTime(2026, 10, 4, 20, 0, 0));

            var week = Assert.Single(stats);
            Assert.Equal(700, week.TotalBytes);
            Assert.Equal(3, week.TotalFiles);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Constructor_InvalidDirectory_DoesNotThrow()
    {
        // Директория на самом деле занята файлом — сервис должен работать без сохранения, а не падать
        var blockingFile = Path.Combine(Path.GetTempPath(), $"stats-blocker-{Guid.NewGuid():N}.bin");
        File.WriteAllText(blockingFile, "x");
        try
        {
            var service = new CleaningStatsService(blockingFile);

            service.RecordSession(1, 1, "test");

            Assert.Equal(1, service.TotalSessions);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }
}
