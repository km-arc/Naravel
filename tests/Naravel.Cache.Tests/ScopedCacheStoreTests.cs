using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache.Scoping;
using Naravel.Cache.Stores;
using Naravel.Cache.Tagging;

namespace Naravel.Cache.Tests;

public sealed class ScopedCacheStoreTests
{
    [Fact]
    public async Task Scopes_isolate_keys_and_can_be_layered_with_tags()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache);
        var scopeA = new ScopedCacheStore(store, "app:user:1");
        var scopeB = new ScopedCacheStore(store, "app:user:2");
        await scopeA.Tags("cart").SetAsync("count", 3, null);

        (await scopeA.Tags("cart").TryGetAsync<int>("count")).Value.Should().Be(3);
        (await scopeB.Tags("cart").TryGetAsync<int>("count")).Found.Should().BeFalse();
    }
}
