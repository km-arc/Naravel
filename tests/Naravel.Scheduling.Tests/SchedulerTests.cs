using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Cache.Locks;
using Naravel.Scheduling.Testing;
using static Naravel.Scheduling.Tests.Tm;

namespace Naravel.Scheduling.Tests;

public class SchedulerTests
{
    private static readonly DateTimeOffset Minute = At("2026-10-26T12:00:00Z");

    [Fact]
    public async Task Only_the_due_tasks_run()
    {
        var ran = new List<string>();
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("every-minute", (_, _) => { lock (ran) ran.Add("every-minute"); return Task.CompletedTask; });
            schedule.Call("hourly", (_, _) => { lock (ran) ran.Add("hourly"); return Task.CompletedTask; }).Hourly();
            schedule.Call("daily-8", (_, _) => { lock (ran) ran.Add("daily-8"); return Task.CompletedTask; }).DailyAt(8, 0);
        });

        await fake.RunAtAsync(Minute).WithTimeout();

        ran.Should().BeEquivalentTo("every-minute", "hourly");
        fake.AssertRan("hourly");
        fake.AssertNotRan("daily-8");
    }

    [Fact]
    public async Task A_failing_task_is_isolated_reported_and_does_not_stop_the_others()
    {
        Exception? seen = null;
        var ranOther = false;
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("bad", (_, _) => throw new InvalidOperationException("boom")).OnFailure(e => { seen = e; return Task.CompletedTask; });
            schedule.Call("good", (_, _) => { ranOther = true; return Task.CompletedTask; });
        });

        await fake.RunAtAsync(Minute).WithTimeout();

        seen.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        ranOther.Should().BeTrue();
        fake.Outcomes.Should().BeEquivalentTo(new[] { new KeyValuePair<string, string>("bad", "failed"), new KeyValuePair<string, string>("good", "succeeded") });
    }

    [Fact]
    public async Task A_task_that_fails_asynchronously_is_also_a_failure()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("bad", async (_, _) =>
        {
            await Task.Yield();
            throw new InvalidOperationException("late");
        }));

        await fake.RunAtAsync(Minute).WithTimeout();

        fake.Outcomes.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, string>("bad", "failed"));
    }

    [Fact]
    public async Task OnSuccess_runs_after_a_good_run_and_not_after_a_failure_and_a_throwing_hook_is_contained()
    {
        var successes = 0;
        var failures = 0;
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("ok", (_, _) => Task.CompletedTask)
                .OnSuccess(() => throw new InvalidOperationException("hook"))
                .OnSuccess(() => { successes++; return Task.CompletedTask; })
                .OnFailure(_ => { failures++; return Task.CompletedTask; });
            schedule.Call("bad", (_, _) => throw new InvalidOperationException("x"))
                .OnSuccess(() => { successes += 100; return Task.CompletedTask; })
                .OnFailure(_ => throw new InvalidOperationException("hook"))
                .OnFailure(_ => { failures++; return Task.CompletedTask; });
        });

        await fake.RunAtAsync(Minute).WithTimeout();

        successes.Should().Be(1);
        failures.Should().Be(1);
        fake.AssertRan("ok");
    }

    [Fact]
    public async Task Without_overlapping_skips_a_run_while_another_instance_is_still_running_then_runs_again()
    {
        var shared = new SharedLock();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        void Configure(Schedule schedule) => schedule.Call("slow", async (_, _) =>
        {
            Interlocked.Increment(ref runs);
            started.TrySetResult();
            await release.Task;
        }).WithoutOverlapping();
        var first = new SchedulerFake(Configure, shared);
        var second = new SchedulerFake(Configure, shared);

        var firstRun = first.RunAtAsync(Minute);
        await started.Task.WithTimeout();
        await second.RunAtAsync(Minute.AddMinutes(1)).WithTimeout();

        second.Outcomes.Should().ContainSingle().Which.Value.Should().Be("skipped_overlap");
        runs.Should().Be(1);

        release.SetResult();
        await firstRun.WithTimeout();
        first.Outcomes.Should().ContainSingle().Which.Value.Should().Be("succeeded");

        await second.RunAtAsync(Minute.AddMinutes(2)).WithTimeout();
        second.Outcomes.Last().Value.Should().Be("succeeded");
        runs.Should().Be(2);
    }

    [Fact]
    public async Task The_overlap_lock_is_released_after_a_failure_and_after_cancellation()
    {
        var shared = new SharedLock();
        var attempt = 0;
        var fake = new SchedulerFake(schedule => schedule.Call("t", async (_, ct) =>
        {
            attempt++;
            if (attempt == 1) throw new InvalidOperationException("first fails");
            if (attempt == 2) await Task.Delay(Timeout.Infinite, ct);
        }).WithoutOverlapping(), shared);

        await fake.RunAtAsync(Minute).WithTimeout();
        using var cts = new CancellationTokenSource();
        var cancelled = fake.RunAtAsync(Minute.AddMinutes(1), cts.Token);
        await Task.Delay(50);
        cts.Cancel();
        await cancelled.WithTimeout();
        await fake.RunAtAsync(Minute.AddMinutes(2)).WithTimeout();

        fake.Outcomes.Select(o => o.Value).Should().Equal("failed", "cancelled", "succeeded");
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task Without_overlapping_uses_the_task_ttl_and_a_crashed_run_cannot_block_for_ever()
    {
        // A run that never releases (a crash) is simulated by taking the lock by hand with a short ttl.
        var shared = new SharedLock();
        var token = await shared.AcquireAsync("schedule:overlap:t", TimeSpan.FromMilliseconds(100));
        token.Should().NotBeNull();
        var runs = 0;
        var fake = new SchedulerFake(schedule => schedule.Call("t", (_, _) => { runs++; return Task.CompletedTask; }).WithoutOverlapping(TimeSpan.FromMinutes(5)), shared);

        await fake.RunAtAsync(Minute).WithTimeout();
        runs.Should().Be(0);

        await Task.Delay(250);
        await fake.RunAtAsync(Minute.AddMinutes(1)).WithTimeout();
        runs.Should().Be(1);
    }

    [Fact]
    public async Task On_one_server_runs_a_due_minute_on_exactly_one_of_two_instances()
    {
        var shared = new SharedLock();
        var runs = 0;
        void Configure(Schedule schedule) => schedule.Call("digest", (_, _) => { Interlocked.Increment(ref runs); return Task.CompletedTask; }).OnOneServer();
        var a = new SchedulerFake(Configure, shared);
        var b = new SchedulerFake(Configure, shared);

        await Task.WhenAll(a.RunAtAsync(Minute), b.RunAtAsync(Minute)).WithTimeout();

        runs.Should().Be(1);
        a.Outcomes.Concat(b.Outcomes).Select(o => o.Value).Order().Should().Equal("skipped_other_server", "succeeded");

        await Task.WhenAll(a.RunAtAsync(Minute.AddMinutes(1)), b.RunAtAsync(Minute.AddMinutes(1))).WithTimeout();
        runs.Should().Be(2, "the next minute is a new slot");
    }

    [Fact]
    public async Task On_one_server_does_not_release_the_slot_so_a_second_pass_in_the_same_minute_is_skipped()
    {
        var shared = new SharedLock();
        var runs = 0;
        var fake = new SchedulerFake(schedule => schedule.Call("t", (_, _) => { runs++; return Task.CompletedTask; }).OnOneServer(), shared);

        await fake.RunAtAsync(Minute).WithTimeout();
        await fake.RunAtAsync(Minute.AddSeconds(30)).WithTimeout();

        runs.Should().Be(1);
        fake.Outcomes.Select(o => o.Value).Should().Equal("succeeded", "skipped_other_server");
    }

    [Fact]
    public void Validate_refuses_on_one_server_with_a_lock_that_is_not_shared()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("digest", (_, _) => Task.CompletedTask).OnOneServer(), new MemoryLock("v"));

        FluentActions.Invoking(fake.Validate).Should().Throw<InvalidOperationException>().WithMessage("*digest*MemoryLock*not shared*");
    }

    [Fact]
    public void Validate_accepts_a_shared_lock_and_accepts_a_local_lock_without_on_one_server()
    {
        new SchedulerFake(schedule => schedule.Call("digest", (_, _) => Task.CompletedTask).OnOneServer(), new SharedLock()).Validate();
        new SchedulerFake(schedule => schedule.Call("prune", (_, _) => Task.CompletedTask).WithoutOverlapping(), new MemoryLock("v")).Validate();
    }

    [Fact]
    public async Task A_predicate_that_throws_skips_only_that_task_for_the_minute()
    {
        var ranOther = false;
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("broken-filter", (_, _) => Task.CompletedTask).When(() => throw new InvalidOperationException("filter"));
            schedule.Call("other", (_, _) => { ranOther = true; return Task.CompletedTask; });
        });

        await fake.RunAtAsync(Minute).WithTimeout();

        ranOther.Should().BeTrue();
        fake.AssertNotRan("broken-filter");
    }

    [Fact]
    public async Task Tasks_run_concurrently_so_a_slow_task_does_not_hold_back_the_others()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var quickDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("slow", async (_, _) => await release.Task);
            schedule.Call("quick", (_, _) => { quickDone.SetResult(); return Task.CompletedTask; });
        });

        var run = fake.RunAtAsync(Minute);
        await quickDone.Task.WithTimeout();
        run.IsCompleted.Should().BeFalse();
        release.SetResult();
        await run.WithTimeout();
    }

    [Fact]
    public void The_assertions_say_what_happened_when_they_fail()
    {
        var fake = new SchedulerFake(schedule => schedule.Call("t", (_, _) => Task.CompletedTask));

        FluentActions.Invoking(() => fake.AssertRan("t")).Should().Throw<InvalidOperationException>().WithMessage("*'t'*none*");
    }

    [Fact]
    public void IsDue_for_an_unknown_task_is_an_error()
    {
        var fake = new SchedulerFake(_ => { });

        FluentActions.Invoking(() => fake.IsDue("nope", Minute)).Should().Throw<ArgumentException>().WithMessage("*nope*");
    }

    [Fact]
    public void The_list_reports_the_next_due_minute_for_every_task()
    {
        var schedule = new Schedule();
        schedule.Call("digest", (_, _) => Task.CompletedTask).DailyAt(8, 0);
        schedule.Call("poll", (_, _) => Task.CompletedTask).EveryFiveMinutes();
        schedule.Call("seven", (_, _) => Task.CompletedTask).Every(TimeSpan.FromMinutes(7));
        var time = new ManualTimeProvider(At("2026-10-24T12:00:30Z"));
        var scheduler = new Scheduler(schedule, new MemoryLock("list"), new EmptyProvider(), new SchedulingOptions(), time, NullLogger.Instance);

        var list = scheduler.List();

        list.Select(i => i.Name).Should().Equal("digest", "poll", "seven");
        list[0].NextRun.Should().Be(At("2026-10-25T08:00:00Z"));
        list[0].Expression.Should().Be("0 8 * * *");
        list[1].NextRun.Should().Be(At("2026-10-24T12:05:00Z"));
        list[2].NextRun!.Value.ToUnixTimeSeconds().Should().Be(list[2].NextRun!.Value.ToUnixTimeSeconds() / 420 * 420).And.BeGreaterThan(At("2026-10-24T12:00:30Z").ToUnixTimeSeconds());
    }

    [Fact]
    public void Options_must_be_positive()
    {
        FluentActions.Invoking(() => new Scheduler(new Schedule(), new MemoryLock("o"), new EmptyProvider(), new SchedulingOptions { OverlapTtl = TimeSpan.Zero }, TimeProvider.System, NullLogger.Instance))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new Scheduler(new Schedule(), new MemoryLock("o"), new EmptyProvider(), new SchedulingOptions { SlotLockTtl = TimeSpan.Zero }, TimeProvider.System, NullLogger.Instance))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Every_run_is_counted_by_task_and_outcome_with_bounded_tags()
    {
        var seen = new List<(string Task, string Outcome)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "Naravel.Scheduling" && instrument.Name == "naravel.scheduling.task.runs") l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            string? task = null, outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "task") task = (string?)tag.Value;
                if (tag.Key == "outcome") outcome = (string?)tag.Value;
            }

            lock (seen) seen.Add((task!, outcome!));
        });
        listener.Start();
        var fake = new SchedulerFake(schedule =>
        {
            schedule.Call("metrics-good", (_, _) => Task.CompletedTask);
            schedule.Call("metrics-bad", (_, _) => throw new InvalidOperationException("secret detail"));
        });

        await fake.RunAtAsync(Minute).WithTimeout();

        lock (seen)
        {
            seen.Should().Contain(("metrics-good", "succeeded"));
            seen.Should().Contain(("metrics-bad", "failed"));
            seen.Where(s => s.Task.StartsWith("metrics-")).Should().OnlyContain(s => s.Outcome == "succeeded" || s.Outcome == "failed");
        }
    }

    private sealed class EmptyProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) => serviceType == typeof(IServiceScopeFactory) ? this : null;

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }
}
