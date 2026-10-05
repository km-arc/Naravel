using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache.Stores;
using Naravel.Cache.Tagging;

namespace Naravel.Cache.Tests;

public sealed class TaggedCacheStoreTests
{
    [Fact]
    public async Task Flushing_a_tag_invalidates_entries_with_that_tag_only()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache, "tests");
        var orders = store.Tags("orders");
        var users = store.Tags("users");
        await orders.SetAsync("latest", "o-1", null);
        await users.SetAsync("latest", "u-1", null);

        await orders.FlushAsync();

        (await orders.TryGetAsync<string>("latest")).Found.Should().BeFalse();
        (await users.TryGetAsync<string>("latest")).Value.Should().Be("u-1");
    }

    [Fact]
    public async Task Tag_reads_and_writes_use_a_stable_initial_version_until_flushed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache, "tests");
        var tagged = store.Tags("orders");

        await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(value => tagged.SetAsync("latest", value, null)));

        var (found, value) = await tagged.TryGetAsync<int>("latest");
        found.Should().BeTrue();
        value.Should().BeInRange(0, 31);
        (await store.TryGetAsync<long>("__naravel_tag_version:orders")).Found.Should().BeFalse();

        await tagged.FlushAsync();

        (await store.TryGetAsync<long>("__naravel_tag_version:orders")).Value.Should().Be(1L);
        (await tagged.TryGetAsync<int>("latest")).Found.Should().BeFalse();
    }
}
