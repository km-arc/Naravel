using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Redis;
using Naravel.Routing;

namespace Naravel.Benchmarks;

/// <summary>
/// The route <c>throttle</c> middleware with the shared store over a real Redis, next to the in-process store (R31.T05, PDR-024).
/// It needs a server: <c>NARAVEL_BENCH_REDIS</c> names it (for example <c>localhost:6379</c>), and without it
/// <c>Program.cs</c> leaves this class out. The evidence workflow starts a Redis service for it.
/// </summary>
/// <remarks>
/// A Redis on the same machine takes the network out of the number, so what this shows is the cost of the client and of one round
/// trip over loopback. A Redis on another host adds its own network time to every request and nothing else.
/// </remarks>
[MemoryDiagnoser]
public class ThrottleRedisBenchmarks
{
    public const string ConnectionVariable = "NARAVEL_BENCH_REDIS";

    private ThrottleScenario _inProcess = null!;
    private ThrottleScenario _sharedOverRedis = null!;

    [GlobalSetup]
    public void Setup()
    {
        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"{ConnectionVariable} names the Redis server this benchmark counts in, for example localhost:6379.");
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Stores:shared:Driver"] = "redis",
            ["Cache:Stores:shared:ConnectionString"] = connection,
            ["Cache:Stores:shared:Prefix"] = "naravel-throttle-bench:" + Guid.NewGuid().ToString("N")
        }).Build();

        _inProcess = new ThrottleScenario(configure: null);
        _sharedOverRedis = new ThrottleScenario(services =>
        {
            services.AddNaravelCache(configuration);
            services.AddNaravelRedisCache(configuration);
            services.AddNaravelSharedThrottle("shared");
        });

        // A store that cannot be reached would not fail a benchmark run: the throttle degrades to the in-process limiter, and the
        // "Redis" row would quietly be the baseline again. One direct call to the store (no guard, no fallback) makes an unreachable
        // server stop the run here instead.
        _ = _sharedOverRedis.Services
            .GetRequiredService<IThrottleStore>()
            .AttemptAsync("benchmark-check|setup|203.0.113.7", 1, TimeSpan.FromMinutes(1), CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    [Benchmark(Baseline = true)]
    public Task InProcessStore() => _inProcess.InvokeAsync();

    [Benchmark]
    public Task SharedStoreOverRedis() => _sharedOverRedis.InvokeAsync();

    [GlobalCleanup]
    public void Cleanup()
    {
        _inProcess.Dispose();
        _sharedOverRedis.Dispose();
    }
}
