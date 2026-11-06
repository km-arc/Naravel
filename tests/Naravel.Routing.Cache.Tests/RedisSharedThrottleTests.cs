using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Redis;
using Naravel.Routing.Tests;
using Naravel.Testing;

namespace Naravel.Routing.Cache.Tests;

/// <summary>
/// The provider job: the same contract, the two-host quota and the burst over a real Redis. Set <c>NARAVEL_TEST_REDIS</c> to a connection
/// string to run them; without it they are reported as skipped, never as passed. Each test uses a key prefix of its own and a clock frozen
/// half way into a minute, so nothing depends on how long Redis keeps a counter and no test can meet a window boundary by chance.
/// </summary>
public static class RedisBackend
{
    public static string NewPrefix() => "naravel-throttle-test:" + Guid.NewGuid().ToString("N");

    private static IConfiguration Configuration(string prefix) => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Cache:Stores:shared:Driver"] = "redis",
        ["Cache:Stores:shared:ConnectionString"] = Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS"),
        ["Cache:Stores:shared:Prefix"] = prefix
    }).Build();

    private static void Register(IServiceCollection services, string prefix, TimeProvider clock)
    {
        var configuration = Configuration(prefix);
        services.AddSingleton(clock);
        services.AddNaravelCache(configuration);
        services.AddNaravelRedisCache(configuration);
        services.AddNaravelSharedThrottle("shared");
    }

    /// <summary>One application instance: its own container, so its own connection to Redis.</summary>
    internal static ServiceProvider Instance(string prefix, TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Register(services, prefix, clock);
        return services.BuildServiceProvider();
    }

    internal static Action<IServiceCollection> Host(string prefix, TimeProvider clock) => services => Register(services, prefix, clock);
}

public sealed class RedisStoreContractTests : ThrottleStoreContract
{
    private readonly string _prefix = RedisBackend.NewPrefix();

    protected override IThrottleStore Create() =>
        Backends.Throttle(Own(RedisBackend.Instance(_prefix, new ManualClock(Backends.MidMinute))));

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Counts_up_to_the_limit_then_rejects() => Counts_up_to_the_limit_then_rejects_Core();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Partitions_are_counted_independently() => Partitions_are_counted_independently_Core();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task A_limit_of_one_admits_exactly_one() => A_limit_of_one_admits_exactly_one_Core();

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Concurrent_attempts_admit_exactly_the_limit() => Concurrent_attempts_admit_exactly_the_limit_Core();
}

public sealed class RedisSharedThrottleTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Two_hosts_on_Redis_share_one_quota_and_a_restarted_host_keeps_it()
    {
        var prefix = RedisBackend.NewPrefix();
        var clock = new ManualClock(Backends.MidMinute);
        void Routes(IRouteRegistrar r) => r.Get("/limited", () => "ok").Middleware("throttle:1,1");
        await using var serverA = await TestApp.StartAsync(routes: Routes, services: RedisBackend.Host(prefix, clock));
        await using var serverB = await TestApp.StartAsync(routes: Routes, services: RedisBackend.Host(prefix, clock));

        ((int)(await serverA.Client.GetAsync("/limited")).StatusCode).Should().Be(200);
        ((int)(await serverA.Client.GetAsync("/limited")).StatusCode).Should().Be(429);
        ((int)(await serverB.Client.GetAsync("/limited")).StatusCode).Should().Be(429, "the other instance counts against the same Redis counter");

        await using var restarted = await TestApp.StartAsync(routes: Routes, services: RedisBackend.Host(prefix, clock));
        ((int)(await restarted.Client.GetAsync("/limited")).StatusCode).Should().Be(429, "the counter lives in Redis, not in the process that was restarted");
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_burst_of_one_subject_over_two_instances_admits_exactly_the_limit()
    {
        var prefix = RedisBackend.NewPrefix();
        var clock = new ManualClock(Backends.MidMinute);
        using var first = RedisBackend.Instance(prefix, clock);
        using var second = RedisBackend.Instance(prefix, clock);
        var stores = new[] { Backends.Throttle(first), Backends.Throttle(second) };
        var key = $"burst|/x|{Guid.NewGuid():N}";

        var decisions = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(i => Task.Run(() => stores[i % 2].AttemptAsync(key, 50, TimeSpan.FromMinutes(1), CancellationToken.None).AsTask())));

        decisions.Count(d => d.Allowed).Should().Be(50, "one counter in Redis, incremented atomically, whichever instance asks");
        decisions.Count(d => !d.Allowed).Should().Be(150);
    }
}
