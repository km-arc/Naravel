using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache.Stores;
using Naravel.Cache.Tagging;
using Naravel.Queue.Drivers;
using Naravel.Queue.Memory;

namespace Naravel.Benchmarks;

[MemoryDiagnoser]
public class QueueAndCacheBenchmarks
{
    private readonly MemoryQueueDriver _queue = new("benchmark");
    private MemoryCacheStore _cache = null!;
    private TaggedCacheStore _tagged = null!;
    private MemoryCache _memory = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _memory = new MemoryCache(new MemoryCacheOptions());
        _cache = new MemoryCacheStore("benchmark", _memory);
        _tagged = new TaggedCacheStore(_cache, ["bench"]);
        await _cache.SetAsync("get", 42, TimeSpan.FromMinutes(5));
        await _tagged.SetAsync("tagged-get", 42, TimeSpan.FromMinutes(5));
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
    public async Task MemoryCacheGetSet()
    {
        await _cache.SetAsync("get", 43, TimeSpan.FromMinutes(5));
        await _cache.TryGetAsync<int>("get");
    }

    [Benchmark]
    public Task<(bool Found, int Value)> TaggedCacheGet() => _tagged.TryGetAsync<int>("tagged-get");

    [GlobalCleanup]
    public void Cleanup() => _memory.Dispose();
}