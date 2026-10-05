using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache;
using Naravel.Cache.Stores;
using Naravel.Cache.Tagging;

namespace Naravel.Cache.Tests;

public sealed class CacheStoreTests
{
    [Fact]
    public async Task Set_get_remember_pull_and_remove_work()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache, "tests");
        var factoryCalls = 0;

        await store.SetAsync("answer", 42, TimeSpan.FromMinutes(1));
        (await store.TryGetAsync<int>("answer")).Should().Be((true, 42));
        (await store.RememberAsync("answer", TimeSpan.FromMinutes(1), () =>
        {
            factoryCalls++;
            return Task.FromResult(0);
        })).Should().Be(42);
        factoryCalls.Should().Be(0);
        (await store.PullAsync<int>("answer")).Should().Be(42);
        (await store.ExistsAsync("answer")).Should().BeFalse();
    }

    [Fact]
    public async Task Counters_are_atomic_and_flush_only_the_store_prefix()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache, "tenant-a");
        var other = new MemoryCacheStore("other", cache, "tenant-b");
        await store.SetAsync("counter", 1L, null);
        await other.SetAsync("counter", 9L, null);

        await store.IncrementAsync("counter", 2);
        await store.DecrementAsync("counter", 1);
        (await store.TryGetAsync<long>("counter")).Value.Should().Be(2);

        await store.FlushAsync();
        (await store.ExistsAsync("counter")).Should().BeFalse();
        (await other.TryGetAsync<long>("counter")).Value.Should().Be(9);
    }

    [Fact]
    public async Task Tagged_and_scoped_decorators_compose_and_flush_by_version()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var backing = new MemoryCacheStore("memory", cache);
        var userA = new Naravel.Cache.Scoping.ScopedCacheStore(backing, "app:user:a");
        var userB = new Naravel.Cache.Scoping.ScopedCacheStore(backing, "app:user:b");
        var taggedA = userA.Tags("orders", "recent");
        var taggedB = userB.Tags("orders", "recent");
        await taggedA.SetAsync("last", "a", null);
        await taggedB.SetAsync("last", "b", null);

        await userA.Tags("orders").FlushAsync();

        (await taggedA.TryGetAsync<string>("last")).Found.Should().BeFalse();
        (await taggedB.TryGetAsync<string>("last")).Value.Should().Be("b");
    }

    [Fact]
    public async Task Pre_cancelled_operations_throw_before_touching_the_store()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var store = new MemoryCacheStore("memory", cache);

        var act = () => store.ExistsAsync("key", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
