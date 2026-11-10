using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;
using Naravel.Scheduling.Testing;
using static Naravel.Scheduling.Tests.Tm;

namespace Naravel.Scheduling.Tests;

// Runs "Quickstart", "Complete example", "Errors" and "Testing" of docs/en/scheduling.md.
public class DocsExampleTests
{
    private static readonly DateTimeOffset Monday8 = new(2026, 10, 26, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Saturday8 = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    private sealed class Digest
    {
        public int Sent;

        public Task SendAsync(CancellationToken ct)
        {
            Sent++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_quickstart_task_gets_a_scope_and_the_time_provider_and_the_host_runs_it()
    {
        var lines = new List<string>();
        var services = new ServiceCollection();
        services.AddNaravelScheduling(schedule =>
        {
            schedule.Call("heartbeat", (sp, ct) =>
            {
                var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
                lines.Add($"{now:HH:mm} tick"); // the guide writes this line to the console
                return Task.CompletedTask;
            }).EveryMinute();
        });
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<Scheduler>().RunDueAsync(At("2026-10-26T12:01:00Z")).WithTimeout();

        lines.Should().ContainSingle().Which.Should().MatchRegex(@"^\d\d:\d\d tick$");
        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void A_task_without_a_frequency_runs_every_minute_and_is_listed_as_such()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("heartbeat", (_, _) => Task.CompletedTask));

        fake.List().Should().ContainSingle().Which.Expression.Should().Be("every minute");
    }

    [Fact]
    public async Task The_complete_example_registers_three_tasks_that_are_due_when_the_guide_says()
    {
        var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var feedsEnabled = true;
        var services = new ServiceCollection();
        services.AddSingleton<Digest>();
        services.AddSingleton<ICacheLock>(new SharedLock()); // the guide uses AddNaravelRedisCache here
        services.AddNaravelScheduling(schedule =>
        {
            schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct))
                .DailyAt(8, 0).Weekdays().InTimeZone(tehran)
                .OnOneServer()
                .OnSuccess(() => Task.CompletedTask)
                .OnFailure(ex => Task.CompletedTask);

            schedule.Call("feed-poll", (sp, ct) => Task.CompletedTask)
                .EveryFiveMinutes()
                .Between(new TimeOnly(8, 0), new TimeOnly(18, 0)).InTimeZone(tehran)
                .When(() => feedsEnabled)
                .WithoutOverlapping();

            schedule.Call("nightly-cleanup", (sp, ct) => Task.CompletedTask)
                .Cron("30 3 * * *").OnOneServer();
        });
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<Scheduler>();
        scheduler.Validate();

        scheduler.List().Select(t => (t.Name, t.Expression)).Should().Equal(
            ("daily-digest", "0 8 * * *"), ("feed-poll", "every 5 minutes"), ("nightly-cleanup", "30 3 * * *"));

        // 08:00 in Tehran (UTC+03:30) is 04:30 UTC: due on a Monday, not on a Saturday.
        scheduler.IsDue("daily-digest", At("2026-10-26T04:30:00Z")).Should().BeTrue();
        scheduler.IsDue("daily-digest", At("2026-10-24T04:30:00Z")).Should().BeFalse();

        // 08:30 and 18:30 Tehran time are inside and outside the window; the flag switches the task off.
        scheduler.IsDue("feed-poll", At("2026-10-26T05:00:00Z")).Should().BeTrue();
        scheduler.IsDue("feed-poll", At("2026-10-26T15:00:00Z")).Should().BeFalse();
        feedsEnabled = false;
        scheduler.IsDue("feed-poll", At("2026-10-26T05:00:00Z")).Should().BeFalse();

        scheduler.IsDue("nightly-cleanup", At("2026-10-26T03:30:00Z")).Should().BeTrue();

        await scheduler.RunDueAsync(At("2026-10-26T04:30:00Z")).WithTimeout();
        provider.GetRequiredService<Digest>().Sent.Should().Be(1);
    }

    [Fact]
    public void Without_InTimeZone_a_frequency_is_read_in_UTC_and_nothing_says_so()
    {
        var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("utc", (_, _) => Task.CompletedTask).DailyAt(8, 0);
            schedule.Call("local", (_, _) => Task.CompletedTask).DailyAt(8, 0).InTimeZone(tehran);
        });

        fake.IsDue("utc", At("2026-10-26T08:00:00Z")).Should().BeTrue();
        fake.IsDue("utc", At("2026-10-26T04:30:00Z")).Should().BeFalse();
        fake.IsDue("local", At("2026-10-26T04:30:00Z")).Should().BeTrue();
    }

    // ---- Testing: the snippets of the guide ----

    [Fact]
    public async Task Digest_runs_on_weekdays_only()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("daily-digest", (sp, ct) => Task.CompletedTask).DailyAt(8, 0).Weekdays());

        fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero)).Should().BeTrue();    // Monday
        fake.IsDue("daily-digest", new DateTimeOffset(2026, 10, 24, 8, 0, 0, TimeSpan.Zero)).Should().BeFalse();   // Saturday

        await fake.RunAtAsync(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
        fake.AssertRan("daily-digest");

        fake.Outcomes.Should().Equal(new KeyValuePair<string, string>("daily-digest", "succeeded"));
    }

    [Fact]
    public async Task Two_fakes_over_one_lock_run_a_one_server_task_once()
    {
        var shared = new MemoryLock(Guid.NewGuid().ToString("N"));   // MemoryLock keeps its state per process, so give each test its own prefix
        Action<Schedule> tasks = schedule => schedule.Call("digest", (sp, ct) => Task.CompletedTask).DailyAt(8, 0).OnOneServer();
        var first = new SchedulerFake(tasks, shared);
        var second = new SchedulerFake(tasks, shared);

        await first.RunAtAsync(Monday8);
        await second.RunAtAsync(Monday8);

        first.Outcomes.Should().Equal(new KeyValuePair<string, string>("digest", "succeeded"));
        second.Outcomes.Should().Equal(new KeyValuePair<string, string>("digest", "skipped_other_server"));
    }

    [Fact]
    public async Task A_task_that_needs_services_is_tested_through_the_real_container()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Digest>();
        services.AddNaravelScheduling(schedule =>
            schedule.Call("daily-digest", (sp, ct) => sp.GetRequiredService<Digest>().SendAsync(ct)).DailyAt(8, 0));
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<Scheduler>().RunDueAsync(Monday8);

        provider.GetRequiredService<Digest>().Sent.Should().Be(1);
    }

    [Fact]
    public async Task The_fake_lists_next_runs_from_the_real_clock_not_from_the_time_you_run_at()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("hourly", (_, _) => Task.CompletedTask).Hourly());
        await fake.RunAtAsync(Saturday8);

        fake.List().Single().NextRun.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}

// The messages of "Errors" in docs/en/scheduling.md. Each assertion is the full text from the source.
public class DocsErrorsTests
{
    private static T Thrown<T>(Action act) where T : Exception => act.Should().Throw<T>().Which;

    private static ScheduledTask NewTask() => new Schedule().Call("t", (_, _) => Task.CompletedTask);

    [Fact]
    public void Call_refuses_a_bad_name_or_a_missing_handler_with_the_messages_in_the_guide()
    {
        var schedule = new Schedule();
        schedule.Call("daily-digest", (_, _) => Task.CompletedTask);

        Action nul = () => schedule.Call(null!, (_, _) => Task.CompletedTask);
        Action blank = () => schedule.Call(" ", (_, _) => Task.CompletedTask);
        nul.Should().ThrowExactly<ArgumentNullException>();
        blank.Should().ThrowExactly<ArgumentException>();

        var tooLong = Thrown<ArgumentException>(() => schedule.Call(new string('a', 101), (_, _) => Task.CompletedTask));
        tooLong.ParamName.Should().Be("name");
        tooLong.Message.Should().StartWith("A task name is at most 100 characters.");

        var characters = Thrown<ArgumentException>(() => schedule.Call("daily digest", (_, _) => Task.CompletedTask));
        characters.ParamName.Should().Be("name");
        characters.Message.Should().StartWith("A task name may contain only ASCII letters, digits and . _ : - (it is used in lock keys and metric tags).");

        var duplicate = Thrown<ArgumentException>(() => schedule.Call("daily-digest", (_, _) => Task.CompletedTask));
        duplicate.ParamName.Should().Be("name");
        duplicate.Message.Should().StartWith("A task named 'daily-digest' is already scheduled.");

        Thrown<ArgumentNullException>(() => schedule.Call("other", null!)).ParamName.Should().Be("handler");
    }

    [Fact]
    public void A_frequency_that_cannot_be_scheduled_is_refused_with_the_messages_in_the_guide()
    {
        var task = NewTask();

        var interval = Thrown<ArgumentOutOfRangeException>(() => task.Every(TimeSpan.FromSeconds(30)));
        interval.ParamName.Should().Be("interval");
        interval.Message.Should().StartWith("The interval must be a whole number of minutes, at least one.");

        var day = Thrown<ArgumentOutOfRangeException>(() => task.WeeklyOn((DayOfWeek)9, 8, 0));
        day.ParamName.Should().Be("day");
        day.Message.Should().StartWith("Not a day of the week.");

        Thrown<ArgumentOutOfRangeException>(() => task.HourlyAt(60));
        Thrown<ArgumentOutOfRangeException>(() => task.DailyAt(24, 0));
        Thrown<ArgumentOutOfRangeException>(() => task.DailyAt(8, 60));
        Thrown<ArgumentOutOfRangeException>(() => task.WithoutOverlapping(TimeSpan.Zero));

        // Six fields (with seconds) and any other text that is not five fields are refused alike.
        foreach (var expression in new[] { "0 0 8 * * *", "daily" })
        {
            var cron = Thrown<ArgumentException>(() => task.Cron(expression));
            cron.ParamName.Should().Be("expression");
            cron.Message.Should().StartWith("Not a valid five-field cron expression (minute hour day-of-month month day-of-week): ");
        }
    }

    [Fact]
    public void A_missing_delegate_or_zone_is_an_ArgumentNullException()
    {
        var task = NewTask();

        Thrown<ArgumentNullException>(() => task.InTimeZone(null!));
        Thrown<ArgumentNullException>(() => task.When(null!));
        Thrown<ArgumentNullException>(() => task.Skip(null!));
        Thrown<ArgumentNullException>(() => task.OnSuccess(null!));
        Thrown<ArgumentNullException>(() => task.OnFailure(null!));
    }

    [Fact]
    public void A_bad_task_is_refused_by_AddNaravelScheduling_itself_not_when_the_host_starts()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddNaravelScheduling(schedule => schedule.Call("bad name", (_, _) => Task.CompletedTask));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_lock_lifetime_that_is_not_positive_is_refused_when_the_scheduler_is_first_created(bool overlap)
    {
        var services = new ServiceCollection();
        services.AddNaravelScheduling(
            schedule => schedule.Call("t", (_, _) => Task.CompletedTask),
            options =>
            {
                if (overlap) options.OverlapTtl = TimeSpan.Zero;
                else options.SlotLockTtl = TimeSpan.FromMinutes(-1);
            });
        using var provider = services.BuildServiceProvider();

        Action create = () => provider.GetRequiredService<Scheduler>();

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task OnOneServer_without_a_shared_lock_stops_the_host_from_starting_with_the_message_in_the_guide()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelScheduling(schedule =>
        {
            schedule.Call("daily-digest", (_, _) => Task.CompletedTask).OnOneServer();
            schedule.Call("nightly-cleanup", (_, _) => Task.CompletedTask).OnOneServer();
            schedule.Call("heartbeat", (_, _) => Task.CompletedTask);
        });
        await using var provider = services.BuildServiceProvider();   // building is fine
        var hosted = provider.GetServices<IHostedService>().Single();

        Func<Task> start = () => hosted.StartAsync(CancellationToken.None);

        (await start.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be(
            "OnOneServer is used by daily-digest, nightly-cleanup, but the cache lock driver 'MemoryLock' is not shared between processes, so every instance would run the task. Configure a shared lock driver (Redis or Memcached).");
    }

    [Fact]
    public void Asking_about_a_task_that_does_not_exist_gives_the_message_in_the_guide()
    {
        var fake = new SchedulerFake(_ => { });

        var error = Thrown<ArgumentException>(() => fake.IsDue("nope", At("2026-10-26T08:00:00Z")));

        error.ParamName.Should().Be("name");
        error.Message.Should().StartWith("No task named 'nope' is scheduled.");
    }

    [Fact]
    public async Task The_fake_assertions_say_what_ran_with_the_messages_in_the_guide()
    {
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("daily-digest", (_, _) => Task.CompletedTask).DailyAt(8, 0);
            schedule.Call("bad", (_, _) => throw new InvalidOperationException("boom"));
        });

        Thrown<InvalidOperationException>(() => fake.AssertRan("daily-digest")).Message.Should().Be(
            "Scheduled task 'daily-digest' did not run successfully. Runs: none.");

        await fake.RunAtAsync(At("2026-10-26T12:00:00Z")).WithTimeout();

        Thrown<InvalidOperationException>(() => fake.AssertRan("bad")).Message.Should().Be(
            "Scheduled task 'bad' did not run successfully. Runs: bad=failed.");
        Thrown<InvalidOperationException>(() => fake.AssertNotRan("bad")).Message.Should().Be(
            "Scheduled task 'bad' was expected not to run. Runs: bad=failed.");
    }

    [Fact]
    public async Task Failures_inside_a_run_are_logged_with_the_messages_in_the_guide_and_nothing_is_thrown()
    {
        var entries = new List<(LogLevel Level, string Message)>();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(new Capture(entries)));
        services.AddNaravelScheduling(schedule =>
        {
            schedule.Call("bad", (_, _) => throw new InvalidOperationException("boom")).OnFailure(_ => throw new InvalidOperationException("hook"));
            schedule.Call("flaky", (_, _) => Task.CompletedTask).When(() => throw new InvalidOperationException("predicate"));
        });
        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<Scheduler>().RunDueAsync(At("2026-10-26T12:00:00Z")).WithTimeout();

        entries.Should().Contain((LogLevel.Error, "Scheduled task bad failed."));
        entries.Should().Contain((LogLevel.Error, "A hook of scheduled task bad failed."));
        entries.Should().Contain((LogLevel.Error, "Evaluating when scheduled task flaky is due failed; the task is skipped for this minute."));
    }

    private sealed class Capture(List<(LogLevel Level, string Message)> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Sink(entries);

        public void Dispose()
        {
        }

        private sealed class Sink(List<(LogLevel Level, string Message)> log) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (log) log.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
