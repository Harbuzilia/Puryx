using SmartCleaner.Core.Cleaning;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Scheduler;
using System.Diagnostics;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// День 19 — срез C: контрактные тесты CleaningSchedulerService через стаб
/// RecordingCommandExecutor. Все операции с планировщиком Windows — schtasks
/// через ICommandExecutor: абсолютный путь (SystemToolLocator), argv-аргументы
/// (ArgumentList), таймауты как в прежнем коде (10 с / 5 с / 5 с).
/// RegisterTask сначала удаляет существующую задачу (/Delete), затем создаёт
/// (/Create) — два запроса к исполнителю.
/// </summary>
public class CleaningSchedulerServiceTests
{
    private const string TaskName = "SmartCleaner_AutoClean";

    private static string CurrentExePath =>
        Process.GetCurrentProcess().MainModule?.FileName ?? "";

    [Fact]
    public async Task RegisterTaskAsync_Weekly_DeleteThenCreateWithEscapedTrArgument()
    {
        // /TR несёт встроенные кавычки: «"…exe" --auto-clean --profile "…"» —
        // аргумент едет ОДНИМ элементом ArgumentList, исполнителем квотуется
        // с экранированием — фактическая командная строка прежняя
        // (риск квотинга из ROADMAP, строка 241).
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(); // /Delete существующей задачи
        executor.EnqueueSuccess(); // /Create
        var scheduler = new CleaningSchedulerService(executor);
        var settings = new ScheduleSettings
        {
            IsEnabled = true,
            Interval = "Weekly",
            DayOfWeek = 1,
            Hour = 3,
            Minute = 0,
            Profile = "Быстрая"
        };

        var result = await scheduler.RegisterTaskAsync(settings);

        Assert.True(result);
        Assert.Equal(2, executor.Requests.Count);

        var delete = executor.Requests[0];
        Assert.Equal(SystemToolLocator.GetSchtasksPath(), delete.FileName);
        Assert.Equal(new[] { "/Delete", "/TN", TaskName, "/F" }, delete.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), delete.Timeout);

        var create = executor.Requests[1];
        Assert.Equal(SystemToolLocator.GetSchtasksPath(), create.FileName);
        Assert.Equal(
            new[]
            {
                "/Create", "/TN", TaskName,
                "/TR", $"\"{CurrentExePath}\" --auto-clean --profile \"Быстрая\"",
                "/SC", "WEEKLY", "/ST", "03:00",
                "/F", "/RL", "LIMITED", "/D", "MON"
            },
            create.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(10), create.Timeout);
    }

    [Fact]
    public async Task RegisterTaskAsync_Disabled_DeletesOnlyWithoutCreate()
    {
        // Отключённый планировщик: только удаление задачи, /Create не выдаётся
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess();
        var scheduler = new CleaningSchedulerService(executor);
        var settings = new ScheduleSettings { IsEnabled = false };

        var result = await scheduler.RegisterTaskAsync(settings);

        Assert.True(result);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(new[] { "/Delete", "/TN", TaskName, "/F" }, request.Arguments);
    }

    [Fact]
    public async Task RegisterTaskAsync_CreateFails_ReturnsFalse()
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueSuccess(); // /Delete
        executor.EnqueueFailure(1); // /Create — отказ
        var scheduler = new CleaningSchedulerService(executor);
        var settings = new ScheduleSettings { IsEnabled = true, Interval = "Daily", Hour = 5, Minute = 30, Profile = "Полное" };

        var result = await scheduler.RegisterTaskAsync(settings);

        Assert.False(result);
        var create = executor.Requests[1];
        Assert.Equal(new[] { "/SC", "DAILY", "/ST", "05:30" }, create.Arguments.Skip(5).Take(4).ToArray());
        Assert.DoesNotContain(create.Arguments, a => a == "/D"); // /D только для Weekly
    }

    [Fact]
    public async Task UnregisterTaskAsync_AlwaysSucceeds_EvenWhenTaskMissing()
    {
        // Прежняя семантика: удаление «OK даже если задачи не было» —
        // ненулевой exit schtasks /Delete не считается сбоем
        var executor = new RecordingCommandExecutor();
        executor.EnqueueFailure(1, "Задача не найдена");
        var scheduler = new CleaningSchedulerService(executor);

        var result = await scheduler.UnregisterTaskAsync();

        Assert.True(result);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(new[] { "/Delete", "/TN", TaskName, "/F" }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task IsTaskRegisteredAsync_ExitCode_MapsToRegistrationState(int exitCode, bool expected)
    {
        var executor = new RecordingCommandExecutor();
        executor.EnqueueResult(new CommandExecutionResult { ExitCode = exitCode });
        var scheduler = new CleaningSchedulerService(executor);

        var result = await scheduler.IsTaskRegisteredAsync();

        Assert.Equal(expected, result);
        var request = Assert.Single(executor.Requests);
        Assert.Equal(new[] { "/Query", "/TN", TaskName }, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }
}
