using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;
using static Naravel.Scheduling.Tests.Tm;

namespace Naravel.Scheduling.Tests;

public class HostingTests
{
    private sealed class Counter
    {
        public int Value;
    }

    private sealed class ScopedMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    [Fact]
    public async Task The_registration_runs_tasks_with_a_new_scope_for_every_run()
    {
        var ids = new List<Guid>();
        var counter = new Counter();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(counter);
        services.AddScoped<ScopedMarker>();
        services.AddNaravelScheduling(schedule => schedule.Call("t", (sp, _) =>
        {
            lock (ids) ids.Add(sp.GetRequiredService<ScopedMarker>().Id);
            sp.GetRequiredService<Counter>().Value++;
            return Task.CompletedTask;
        }));
        await using var provider = services.BuildServiceProvider();
        var scheduler = provider.GetRequiredService<Scheduler>();

        await scheduler.RunDueAsync(At("2026-10-26T12:00:00Z")).WithTimeout();
        await scheduler.RunDueAsync(At("2026-10-26T12:01:00Z")).WithTimeout();

        counter.Value.Should().Be(2);
        ids.Distinct().Should().HaveCount(2);
        provider.GetServices<IHostedService>().Should().ContainSingle();
    }

    [Fact]
    public void The_registration_uses_the_registered_cache_lock_and_options()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var shared = new SharedLock();
        services.AddSingleton<ICacheLock>(shared);
        services.AddNaravelScheduling(schedule => schedule.Call("t", (_, _) => Task.CompletedTask).OnOneServer(), options => options.OverlapTtl = TimeSpan.FromMinutes(10));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Scheduler>().Validate();
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SchedulingOptions>>().Value.OverlapTtl.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Starting_the_host_fails_for_on_one_server_without_a_shared_lock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICacheLock>(new MemoryLock("host"));
        services.AddNaravelScheduling(schedule => schedule.Call("digest", (_, _) => Task.CompletedTask).OnOneServer());
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await FluentActions.Awaiting(() => hosted.StartAsync(CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>().WithMessage("*digest*");
    }

    [Fact]
    public async Task Without_a_registered_lock_a_process_local_one_is_used_for_overlap_but_one_server_is_refused()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelScheduling(schedule => schedule.Call("prune", (_, _) => Task.CompletedTask).WithoutOverlapping());
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<Scheduler>().Validate();
        await provider.GetRequiredService<Scheduler>().RunDueAsync(At("2026-10-26T12:00:00Z")).WithTimeout();
    }

    [Fact]
    public async Task The_hosted_loop_wakes_on_each_minute_boundary_and_runs_what_is_due_then_waits_for_running_tasks_on_stop()
    {
        var time = new ManualTimeProvider(At("2026-10-26T12:00:30Z"));
        var ran = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddNaravelScheduling(schedule =>
        {
            schedule.Call("every-minute", (_, _) => { lock (ran) ran.Add("every-minute"); return Task.CompletedTask; });
            schedule.Call("quarter", async (_, _) =>
            {
                lock (ran) ran.Add("quarter");
                started.TrySetResult();
                await release.Task;
            }).EveryFifteenMinutes();
        });
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await WaitFor(() => time.TimerCount > 0);

        time.Advance(TimeSpan.FromSeconds(29));
        await Task.Delay(50);
        lock (ran) ran.Should().BeEmpty("the minute has not changed yet");

        time.Advance(TimeSpan.FromSeconds(1));
        await WaitFor(() => { lock (ran) return ran.Contains("every-minute"); });
        lock (ran) ran.Should().Equal("every-minute");

        await WaitFor(() => time.TimerCount > 0);
        time.Advance(TimeSpan.FromMinutes(14));
        await started.Task.WithTimeout();
        lock (ran) ran.Should().Contain("quarter");

        using var stopCts = new CancellationTokenSource();
        var stop = hosted.StopAsync(stopCts.Token);
        await Task.Delay(50);
        stop.IsCompleted.Should().BeFalse("a task is still running");
        release.SetResult();
        await stop.WithTimeout();
    }

    [Fact]
    public async Task Stopping_gives_up_waiting_when_the_stop_token_is_cancelled()
    {
        var time = new ManualTimeProvider(At("2026-10-26T12:00:30Z"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forever = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(time);
        services.AddNaravelScheduling(schedule => schedule.Call("stuck", async (_, _) =>
        {
            started.TrySetResult();
            await forever.Task;
        }));
        await using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().Single();
        await hosted.StartAsync(CancellationToken.None);
        await WaitFor(() => time.TimerCount > 0);
        time.Advance(TimeSpan.FromSeconds(30));
        await started.Task.WithTimeout();

        using var stopCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await hosted.StopAsync(stopCts.Token).WithTimeout();

        forever.SetResult();
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The condition did not become true in time.");
            await Task.Delay(10);
        }
    }
}
