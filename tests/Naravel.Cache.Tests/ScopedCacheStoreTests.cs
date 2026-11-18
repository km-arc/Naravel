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

    // Accepted by the owner (finding CCH-009): a key is joined to the scope prefix with a colon and nothing else, as Laravel joins a prefix and a key.
    // A change here is a decision about stored keys, so it has to be made on purpose.
    [Fact]
    public async Task A_key_with_the_scope_separator_names_the_same_entry_as_a_nested_scope_but_never_a_sibling()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new MemoryCacheStore("memory", cache);
        var outer = new ScopedCacheStore(store, "app");
        var nested = new ScopedCacheStore(store, "app:user:7");
        var sibling = new ScopedCacheStore(store, "app:user:70");
        await outer.SetAsync("user:7:profile", "written by the outer view", TimeSpan.FromMinutes(1));

        (await nested.TryGetAsync<string>("profile")).Value.Should().Be("written by the outer view");
        (await sibling.TryGetAsync<string>("profile")).Found.Should().BeFalse();
    }
}
