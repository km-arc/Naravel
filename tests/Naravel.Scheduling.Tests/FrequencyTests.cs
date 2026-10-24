using Naravel.Scheduling.Testing;
using static Naravel.Scheduling.Tests.Tm;

namespace Naravel.Scheduling.Tests;

public class FrequencyTests
{
    private static bool Due(Func<ScheduledTask, ScheduledTask> frequency, string minute)
    {
        var fake = new SchedulerFake(schedule => frequency(schedule.Call("t", (_, _) => Task.CompletedTask)));
        return fake.IsDue("t", At(minute));
    }

    [Theory]
    [InlineData("2026-10-26T12:00:00Z", true)]
    [InlineData("2026-10-26T12:01:00Z", true)]
    public void A_task_with_no_frequency_runs_every_minute(string minute, bool expected)
        => Due(t => t, minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:00:00Z", true)]
    [InlineData("2026-10-26T12:05:00Z", true)]
    [InlineData("2026-10-26T12:03:00Z", false)]
    public void Every_five_minutes(string minute, bool expected)
        => Due(t => t.EveryFiveMinutes(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:10:00Z", true)]
    [InlineData("2026-10-26T12:15:00Z", false)]
    public void Every_ten_minutes(string minute, bool expected)
        => Due(t => t.EveryTenMinutes(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:45:00Z", true)]
    [InlineData("2026-10-26T12:50:00Z", false)]
    public void Every_fifteen_minutes(string minute, bool expected)
        => Due(t => t.EveryFifteenMinutes(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:30:00Z", true)]
    [InlineData("2026-10-26T12:15:00Z", false)]
    public void Every_thirty_minutes(string minute, bool expected)
        => Due(t => t.EveryThirtyMinutes(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:00:00Z", true)]
    [InlineData("2026-10-26T12:01:00Z", false)]
    public void Hourly(string minute, bool expected)
        => Due(t => t.Hourly(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T12:15:00Z", true)]
    [InlineData("2026-10-26T12:00:00Z", false)]
    public void Hourly_at_a_minute(string minute, bool expected)
        => Due(t => t.HourlyAt(15), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T00:00:00Z", true)]
    [InlineData("2026-10-26T00:01:00Z", false)]
    [InlineData("2026-10-26T12:00:00Z", false)]
    public void Daily_is_midnight(string minute, bool expected)
        => Due(t => t.Daily(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T08:00:00Z", true)]
    [InlineData("2026-10-26T08:01:00Z", false)]
    [InlineData("2026-10-26T20:00:00Z", false)]
    public void Daily_at_a_time(string minute, bool expected)
        => Due(t => t.DailyAt(8, 0), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-25T00:00:00Z", true)]
    [InlineData("2026-10-26T00:00:00Z", false)]
    public void Weekly_is_sunday_midnight(string minute, bool expected)
        => Due(t => t.Weekly(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T09:30:00Z", true)]
    [InlineData("2026-10-27T09:30:00Z", false)]
    [InlineData("2026-10-26T09:31:00Z", false)]
    public void Weekly_on_a_day_and_time(string minute, bool expected)
        => Due(t => t.WeeklyOn(DayOfWeek.Monday, 9, 30), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-11-01T00:00:00Z", true)]
    [InlineData("2026-11-02T00:00:00Z", false)]
    [InlineData("2026-11-01T00:01:00Z", false)]
    public void Monthly_is_the_first_at_midnight(string minute, bool expected)
        => Due(t => t.Monthly(), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T09:20:00Z", true)]
    [InlineData("2026-10-26T09:21:00Z", false)]
    [InlineData("2026-10-26T18:00:00Z", false)]
    [InlineData("2026-10-24T09:20:00Z", false)]
    public void A_cron_expression(string minute, bool expected)
        => Due(t => t.Cron("*/20 9-17 * * 1-5"), minute).Should().Be(expected);

    [Fact]
    public void Every_a_whole_number_of_minutes_is_due_once_in_each_window_and_the_same_on_every_instance()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).Every(TimeSpan.FromMinutes(7)));
        var start = At("2026-10-26T12:00:00Z");

        var due = Enumerable.Range(0, 70).Select(i => start.AddMinutes(i)).Where(minute => fake.IsDue("t", minute)).ToList();

        due.Should().HaveCount(10);
        due.Zip(due.Skip(1), (a, b) => b - a).Should().OnlyContain(gap => gap == TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void Seconds_inside_a_minute_do_not_matter()
        => Due(t => t.DailyAt(8, 0), "2026-10-26T08:00:42Z").Should().BeTrue();

    [Fact]
    public void Weekdays_skips_saturday_and_sunday()
    {
        Due(t => t.Daily().Weekdays(), "2026-10-26T00:00:00Z").Should().BeTrue();
        Due(t => t.Daily().Weekdays(), "2026-10-24T00:00:00Z").Should().BeFalse();
        Due(t => t.EveryMinute().Weekdays(), "2026-10-25T10:00:00Z").Should().BeFalse();
    }

    [Theory]
    [InlineData("2026-10-26T07:59:00Z", false)]
    [InlineData("2026-10-26T08:00:00Z", true)]
    [InlineData("2026-10-26T17:00:00Z", true)]
    [InlineData("2026-10-26T17:01:00Z", false)]
    public void Between_includes_both_ends(string minute, bool expected)
        => Due(t => t.Between(new TimeOnly(8, 0), new TimeOnly(17, 0)), minute).Should().Be(expected);

    [Theory]
    [InlineData("2026-10-26T23:00:00Z", true)]
    [InlineData("2026-10-26T01:59:00Z", true)]
    [InlineData("2026-10-26T03:00:00Z", false)]
    public void Between_may_cross_midnight(string minute, bool expected)
        => Due(t => t.Between(new TimeOnly(22, 0), new TimeOnly(2, 0)), minute).Should().Be(expected);

    [Fact]
    public void When_and_Skip_predicates_decide_every_time_they_are_asked()
    {
        var open = true;
        var paused = false;
        var fake = new SchedulerFake(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).When(() => open).Skip(() => paused));
        var minute = At("2026-10-26T12:00:00Z");

        fake.IsDue("t", minute).Should().BeTrue();
        open = false;
        fake.IsDue("t", minute).Should().BeFalse();
        open = true;
        paused = true;
        fake.IsDue("t", minute).Should().BeFalse();
    }

    [Fact]
    public void A_time_zone_moves_the_local_time_and_follows_daylight_saving()
    {
        var newYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        // 2026-10-26 is still daylight time (UTC-4); 2026-11-02 is standard time (UTC-5).
        Due(t => t.DailyAt(8, 0).InTimeZone(newYork), "2026-10-26T12:00:00Z").Should().BeTrue();
        Due(t => t.DailyAt(8, 0).InTimeZone(newYork), "2026-10-26T08:00:00Z").Should().BeFalse();
        Due(t => t.DailyAt(8, 0).InTimeZone(newYork), "2026-11-02T13:00:00Z").Should().BeTrue();
        Due(t => t.DailyAt(8, 0).InTimeZone(newYork), "2026-11-02T12:00:00Z").Should().BeFalse();
    }

    [Fact]
    public void Weekdays_and_Between_are_read_in_the_task_time_zone()
    {
        var plus12 = TimeZoneInfo.CreateCustomTimeZone("plus12", TimeSpan.FromHours(12), "plus12", "plus12");

        // 2026-10-25T20:00Z (Sunday evening in UTC) is Monday 08:00 at UTC+12.
        Due(t => t.EveryMinute().Weekdays().Between(new TimeOnly(8, 0), new TimeOnly(9, 0)).InTimeZone(plus12), "2026-10-25T20:00:00Z").Should().BeTrue();
        Due(t => t.EveryMinute().Weekdays().InTimeZone(TimeZoneInfo.Utc), "2026-10-25T20:00:00Z").Should().BeFalse();
    }

    [Fact]
    public void The_expression_is_reported_as_text()
    {
        var schedule = new Schedule();
        schedule.Call("a", (_, _) => Task.CompletedTask).DailyAt(8, 5).Expression.Should().Be("5 8 * * *");
        schedule.Call("b", (_, _) => Task.CompletedTask).EveryFiveMinutes().Expression.Should().Be("every 5 minutes");
        schedule.Call("c", (_, _) => Task.CompletedTask).Expression.Should().Be("every minute");
        schedule.Call("d", (_, _) => Task.CompletedTask).Cron(" 0 3 * * 1 ").Expression.Should().Be("0 3 * * 1");
    }

    [Theory]
    [InlineData("not a cron")]
    [InlineData("* * * * * *")]
    [InlineData("61 * * * *")]
    public void An_invalid_cron_expression_is_refused(string expression)
    {
        var task = new Schedule().Call("t", (_, _) => Task.CompletedTask);

        FluentActions.Invoking(() => task.Cron(expression)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Out_of_range_arguments_are_refused()
    {
        var task = new Schedule().Call("t", (_, _) => Task.CompletedTask);

        FluentActions.Invoking(() => task.HourlyAt(60)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.HourlyAt(-1)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.DailyAt(24, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.DailyAt(0, 60)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.WeeklyOn((DayOfWeek)9, 0, 0)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.Every(TimeSpan.FromSeconds(30))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.Every(TimeSpan.FromSeconds(90))).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => task.WithoutOverlapping(TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("has space")]
    [InlineData("slash/name")]
    [InlineData("persî")]
    public void A_task_name_must_be_safe_for_lock_keys_and_metric_tags(string name)
    {
        FluentActions.Invoking(() => new Schedule().Call(name, (_, _) => Task.CompletedTask)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_task_name_is_unique_and_limited_in_length()
    {
        var schedule = new Schedule();
        schedule.Call("daily-digest", (_, _) => Task.CompletedTask);

        FluentActions.Invoking(() => schedule.Call("daily-digest", (_, _) => Task.CompletedTask)).Should().Throw<ArgumentException>().WithMessage("*already scheduled*");
        FluentActions.Invoking(() => schedule.Call(new string('a', 101), (_, _) => Task.CompletedTask)).Should().Throw<ArgumentException>();
        schedule.Call("a.b_c:d-1", (_, _) => Task.CompletedTask).Name.Should().Be("a.b_c:d-1");
        schedule.Tasks.Select(t => t.Name).Should().Equal("daily-digest", "a.b_c:d-1");
    }
}
