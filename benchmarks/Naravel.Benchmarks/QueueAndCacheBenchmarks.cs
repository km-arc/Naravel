using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache;
using Naravel.Cache.RateLimiting;
using Naravel.Cache.Stores;
using Naravel.Cache.Tagging;
using Naravel.Queue.Drivers;
using Naravel.Queue.Failed;
using Naravel.Queue.Memory;

namespace Naravel.Benchmarks;

[MemoryDiagnoser]
public class QueueAndCacheBenchmarks
{
    private readonly MemoryQueueDriver _queue = new("benchmark");
    private readonly InMemoryFailedJobStore _failedJobs = new();
    private MemoryCacheStore _cache = null!;
    private TaggedCacheStore _tagged = null!;
    private MemoryCache _memory = null!;
    private ServiceProvider _rateLimiterProvider = null!;
    private IRateLimiter _rateLimiter = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _memory = new MemoryCache(new MemoryCacheOptions());
        _cache = new MemoryCacheStore("benchmark", _memory);
        _tagged = new TaggedCacheStore(_cache, ["bench"]);
        await _cache.SetAsync("get", 42, TimeSpan.FromMinutes(5));
        await _tagged.SetAsync("tagged-get", 42, TimeSpan.FromMinutes(5));

        var configuration = new ConfigurationManager
        {
            ["Cache:Default"] = "memory",
            ["Cache:AppPrefix"] = "benchmark",
            ["Cache:Stores:memory:Driver"] = "memory",
            ["Cache:Stores:memory:Prefix"] = "benchmark"
        };
        var services = new ServiceCollection();
        services.AddNaravelCache(configuration).AddNaravelRateLimiter("memory");
        _rateLimiterProvider = services.BuildServiceProvider();
        _rateLimiter = _rateLimiterProvider.GetRequiredService<IRateLimiter>();
    }

    [Benchmark]
    public async Task MemoryQueuePushPop()
    {
        var message = new QueuedMessage { Queue = "bench", JobType = "bench", Payload = "{}" };
        await _queue.PushAsync(message);
        var popped = await _queue.PopAsync("bench");
        await _queue.AckAsync(popped!);
    }

    [Benchmark]
    public async Task FailedJobStoreRecordListForget()
    {
        var message = new QueuedMessage { Id = "benchmark-failed", Queue = "bench", JobType = "bench", Payload = "{}" };
        await _failedJobs.RecordAsync(message, new InvalidOperationException("benchmark"), CancellationToken.None);
        _ = await _failedJobs.ListAsync(CancellationToken.None);
        await _failedJobs.ForgetAsync(message.Id, CancellationToken.None);
    }

    [Benchmark]
    public async Task MemoryCacheGetSet()
    {
        await _cache.SetAsync("get", 43, TimeSpan.FromMinutes(5));
        await _cache.TryGetAsync<int>("get");
    }

    [Benchmark]
    public Task<(bool Found, int Value)> TaggedCacheGet() => _tagged.TryGetAsync<int>("tagged-get");

    [Benchmark]
    public Task<RateLimitDecision> MemoryRateLimiterAttempt() =>
        _rateLimiter.AttemptAsync("benchmark", "shared-subject", long.MaxValue, TimeSpan.FromMinutes(1));

    [GlobalCleanup]
    public void Cleanup()
    {
        _rateLimiterProvider.Dispose();
        _memory.Dispose();
    }
}