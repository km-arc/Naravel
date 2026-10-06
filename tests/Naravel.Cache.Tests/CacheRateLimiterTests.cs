using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache;
using Naravel.Cache.RateLimiting;
using Naravel.Cache.Testing;

namespace Naravel.Cache.Tests;

[CollectionDefinition("Cache rate limiter telemetry", DisableParallelization = true)]
public sealed class CacheRateLimiterTelemetryCollection { }

public class CacheRateLimiterTests
{
    [Fact]
    public async Task Fixed_window_admits_limit_rejects_next_and_expires_from_first_attempt()
    {
        using var provider = BuildProvider("memory");
        var limiter = provider.GetRequiredService<IRateLimiter>();
        var window = TimeSpan.FromSeconds(2);

        var first = await limiter.AttemptAsync("login", "subject-1", 2, window);
        var second = await limiter.AttemptAsync("login", "subject-1", 2, window);
        var rejected = await limiter.AttemptAsync("login", "subject-1", 2, window);

        first.Allowed.Should().BeTrue();
        first.Remaining.Should().Be(1);
        second.Allowed.Should().BeTrue();
        second.Remaining.Should().Be(0);
        rejected.Allowed.Should().BeFalse();
        rejected.Remaining.Should().Be(0);
        rejected.RetryAfter.Should().BePositive();

        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var stillRejected = await limiter.AttemptAsync("login", "subject-1", 2, window);
        stillRejected.Allowed.Should().BeFalse();
        stillRejected.RetryAfter.Should().BeLessThan(rejected.RetryAfter);

        await Task.Delay(TimeSpan.FromMilliseconds(1050));
        (await limiter.AttemptAsync("login", "subject-1", 2, window)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Clear_removes_the_current_window()
    {
        using var provider = BuildProvider("memory");
        var limiter = provider.GetRequiredService<IRateLimiter>();
        (await limiter.AttemptAsync("login", "subject-2", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
    (await limiter.AttemptAsync("login", "subject-2", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeFalse();

        await limiter.ClearAsync("login", "subject-2");

        (await limiter.AttemptAsync("login", "subject-2", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Named_stores_keep_rate_limits_isolated()
    {
        using var provider = BuildProvider("primary", "secondary");
        var cacheManager = provider.GetRequiredService<CacheManager>();
        var lockManager = provider.GetRequiredService<LockManager>();
        var primary = new CacheRateLimiter(cacheManager, lockManager, "primary");
        var secondary = new CacheRateLimiter(cacheManager, lockManager, "secondary");

        (await primary.AttemptAsync("api", "subject", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        (await primary.AttemptAsync("api", "subject", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeFalse();
        (await secondary.AttemptAsync("api", "subject", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task Fake_records_attempts_exposes_state_and_resets()
    {
        var fake = new RateLimiterFake();
        var result = await fake.AttemptAsync("upload", "account-7", 2, TimeSpan.FromMinutes(1));

        result.Allowed.Should().BeTrue();
        result.Remaining.Should().Be(1);
        fake.GetAttemptCount("upload", "account-7").Should().Be(1);
        fake.AssertAttempted("upload", "account-7");
        fake.Reset();
        fake.GetAttemptCount("upload", "account-7").Should().Be(0);
        fake.Attempts.Should().BeEmpty();
    }

    [Fact]
    public async Task Pre_cancelled_attempt_throws_before_mutating_the_window()
    {
        using var provider = BuildProvider("memory");
        var limiter = provider.GetRequiredService<IRateLimiter>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => limiter.AttemptAsync("login", "subject-3", 1, TimeSpan.FromMinutes(1), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await limiter.AttemptAsync("login", "subject-3", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
    }

    internal static ServiceProvider BuildProvider(params string[] stores)
    {
        var values = new Dictionary<string, string?>
        {
            ["Cache:Default"] = stores[0],
            ["Cache:AppPrefix"] = "rate-limit-tests"
        };
        foreach (var store in stores)
        {
            values[$"Cache:Stores:{store}:Driver"] = "memory";
            values[$"Cache:Stores:{store}:Prefix"] = store;
        }

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values);
        var services = new ServiceCollection();
        services.AddNaravelCache(configuration).AddNaravelRateLimiter(stores[0]);
        return services.BuildServiceProvider();
    }
}

[Collection("Cache rate limiter telemetry")]
public class CacheRateLimiterTelemetryTests
{
    [Fact]
    public async Task Meter_records_allowed_rejected_duration_and_activity()
    {
        var counts = new ConcurrentDictionary<string, long>();
        var durations = 0;
        var activities = new ConcurrentQueue<Activity>();
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Naravel.Cache") listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            counts.AddOrUpdate(instrument.Name, value, (_, current) => current + value));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            if (instrument.Name == "naravel.cache.ratelimiter.duration") Interlocked.Increment(ref durations);
        });
        meterListener.Start();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Naravel.Cache",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(activityListener);
        using var provider = CacheRateLimiterTests.BuildProvider("memory");
        var limiter = provider.GetRequiredService<IRateLimiter>();
        await limiter.AttemptAsync("metric-test", "subject", 1, TimeSpan.FromMinutes(1));
        await limiter.AttemptAsync("metric-test", "subject", 1, TimeSpan.FromMinutes(1));

        counts["naravel.cache.ratelimiter.allowed"].Should().Be(1);
        counts["naravel.cache.ratelimiter.rejected"].Should().Be(1);
        durations.Should().Be(2);
        activities.Should().HaveCount(2);
        activities.Select(activity => activity.GetTagItem("naravel.cache.outcome")?.ToString())
            .Should().Contain(new[] { "allowed", "rejected" });
    }
}
