using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache;
using Naravel.Cache.Memcached;
using Naravel.Cache.RateLimiting;
using Naravel.Cache.Redis;
using Naravel.Testing;

namespace Naravel.Cache.Tests;

/// <summary>
/// The rate limiter against real Redis and Memcached servers (CI "services" job; skipped unless NARAVEL_TEST_REDIS / NARAVEL_TEST_MEMCACHED is set).
/// Two independent service providers sharing one backend stand for two replicas of an application: they must share one limit.
/// </summary>
public abstract class RateLimiterServerTests
{
    private const string Driver_Redis = "redis";

    protected abstract string Driver { get; }
    protected abstract string EnvironmentVariable { get; }

    protected ServiceProvider Replica(string prefix)
    {
        var endpoint = Environment.GetEnvironmentVariable(EnvironmentVariable)!;
        var values = new Dictionary<string, string?>
        {
            ["Cache:Default"] = "shared",
            ["Cache:AppPrefix"] = prefix,
            ["Cache:Stores:shared:Driver"] = Driver,
            ["Cache:Stores:shared:Prefix"] = "rl",
            [Driver == Driver_Redis ? "Cache:Stores:shared:ConnectionString" : "Cache:Stores:shared:Servers"] = endpoint,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        var cache = services.AddNaravelCache(configuration);
        if (Driver == Driver_Redis) cache.AddNaravelRedisCache(configuration);
        else cache.AddNaravelMemcachedCache(configuration);
        cache.AddNaravelRateLimiter("shared");
        return services.BuildServiceProvider();
    }

    protected static string NewPrefix() => "rl-test:" + Guid.NewGuid().ToString("N");

    protected async Task Two_replicas_share_one_limit_under_concurrency()
    {
        var prefix = NewPrefix();
        await using var a = Replica(prefix);
        await using var b = Replica(prefix);
        var limiters = new[] { a.GetRequiredService<IRateLimiter>(), b.GetRequiredService<IRateLimiter>() };

        var decisions = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => Task.Run(() => limiters[i % 2].AttemptAsync("burst", "subject", 10, TimeSpan.FromMinutes(1)))));

        decisions.Count(d => d.Allowed).Should().Be(10, "both replicas count against the same atomic backend counter");
        decisions.Should().OnlyContain(
            d => d.RetryAfter > TimeSpan.Zero && d.RetryAfter <= TimeSpan.FromMinutes(1),
            "every decision carries a usable retry-after even while the first attempt is still writing the start of the window");
    }

    protected async Task The_window_expires_on_the_server_and_a_new_one_starts()
    {
        await using var replica = Replica(NewPrefix());
        var limiter = replica.GetRequiredService<IRateLimiter>();
        var window = TimeSpan.FromSeconds(2);

        (await limiter.AttemptAsync("login", "u", 1, window)).Allowed.Should().BeTrue();
        var rejected = await limiter.AttemptAsync("login", "u", 1, window);
        rejected.Allowed.Should().BeFalse();
        rejected.RetryAfter.Should().BePositive();

        await Task.Delay(TimeSpan.FromSeconds(3.2));

        (await limiter.AttemptAsync("login", "u", 1, window)).Allowed.Should().BeTrue("the counter expired on the server instead of living forever");
    }

    // Every attempt, rejected ones included, now increments the counter. The window must still end at the time the first attempt fixed:
    // if the server let an increment of an existing counter move its expiry, a caller that keeps trying would keep the window open for ever.
    // The server may notice an expired Memcached item up to a second late, hence the margins: the window ends 3 s after the first attempt,
    // a window slid forward by the attempt at 2 s would last until 5 s, and the last attempt is made at 4.4 s.
    protected async Task Attempts_inside_the_window_do_not_extend_it_on_the_server()
    {
        await using var replica = Replica(NewPrefix());
        var limiter = replica.GetRequiredService<IRateLimiter>();
        var window = TimeSpan.FromSeconds(3);
        var clock = Stopwatch.StartNew();

        (await limiter.AttemptAsync("slide", "u", 1, window)).Allowed.Should().BeTrue();
        await DelayUntil(clock, TimeSpan.FromSeconds(2));
        (await limiter.AttemptAsync("slide", "u", 1, window)).Allowed.Should().BeFalse();
        await DelayUntil(clock, TimeSpan.FromSeconds(4.4));

        (await limiter.AttemptAsync("slide", "u", 1, window)).Allowed.Should().BeTrue("a rejected attempt must not move the end of the window");
    }

    private static Task DelayUntil(Stopwatch clock, TimeSpan moment)
    {
        var left = moment - clock.Elapsed;
        return left > TimeSpan.Zero ? Task.Delay(left) : Task.CompletedTask;
    }

    protected async Task Clear_on_one_replica_resets_the_bucket_for_the_other()
    {
        var prefix = NewPrefix();
        await using var a = Replica(prefix);
        await using var b = Replica(prefix);
        var first = a.GetRequiredService<IRateLimiter>();
        var second = b.GetRequiredService<IRateLimiter>();

        (await first.AttemptAsync("login", "u", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        (await second.AttemptAsync("login", "u", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeFalse();

        await first.ClearAsync("login", "u");

        (await second.AttemptAsync("login", "u", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
    }

    protected async Task Subjects_and_limiters_are_isolated_on_the_server()
    {
        await using var replica = Replica(NewPrefix());
        var limiter = replica.GetRequiredService<IRateLimiter>();

        (await limiter.AttemptAsync("login", "u1", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        (await limiter.AttemptAsync("login", "u2", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        (await limiter.AttemptAsync("upload", "u1", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeTrue();
        (await limiter.AttemptAsync("login", "u1", 1, TimeSpan.FromMinutes(1))).Allowed.Should().BeFalse();
    }
}

public sealed class RedisRateLimiterServerTests : RateLimiterServerTests
{
    protected override string Driver => "redis";
    protected override string EnvironmentVariable => "NARAVEL_TEST_REDIS";

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Replicas_share_one_limit() => Two_replicas_share_one_limit_under_concurrency();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Window_expires() => The_window_expires_on_the_server_and_a_new_one_starts();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Clear_is_shared() => Clear_on_one_replica_resets_the_bucket_for_the_other();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Attempts_do_not_extend_the_window() => Attempts_inside_the_window_do_not_extend_it_on_the_server();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Isolation() => Subjects_and_limiters_are_isolated_on_the_server();
}

public sealed class MemcachedRateLimiterServerTests : RateLimiterServerTests
{
    protected override string Driver => "memcached";
    protected override string EnvironmentVariable => "NARAVEL_TEST_MEMCACHED";

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public Task Replicas_share_one_limit() => Two_replicas_share_one_limit_under_concurrency();

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public Task Window_expires() => The_window_expires_on_the_server_and_a_new_one_starts();

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public Task Clear_is_shared() => Clear_on_one_replica_resets_the_bucket_for_the_other();

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public Task Attempts_do_not_extend_the_window() => Attempts_inside_the_window_do_not_extend_it_on_the_server();

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public Task Isolation() => Subjects_and_limiters_are_isolated_on_the_server();

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task A_ttl_increment_expires_on_the_server_instead_of_becoming_permanent()
    {
        var prefix = NewPrefix();
        await using var replica = Replica(prefix);
        var store = replica.GetRequiredService<CacheManager>().Store("shared");

        (await store.IncrementAsync("c", 1, TimeSpan.FromSeconds(2))).Should().Be(1);
        (await store.IncrementAsync("c", 1, TimeSpan.FromSeconds(2))).Should().Be(2);

        await Task.Delay(TimeSpan.FromSeconds(3.2));

        (await store.IncrementAsync("c", 1, TimeSpan.FromSeconds(2))).Should().Be(1, "the window ended and the counter restarted");
    }
}
