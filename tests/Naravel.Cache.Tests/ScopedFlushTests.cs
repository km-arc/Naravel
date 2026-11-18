using Enyim.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Memcached;
using Naravel.Cache.Redis;
using Naravel.Cache.Scoping;
using Naravel.Cache.Stores;
using Naravel.Cache.Testing;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

// AV-001 / CCH-001: flushing one scope (a user, a tenant) removes the keys of that scope and nothing else, and a store that
// cannot do that refuses instead of clearing more than it was asked to.
public sealed class ScopedFlushTests : IDisposable
{
    private readonly MemoryCache _memory = new(new MemoryCacheOptions());

    public void Dispose() => _memory.Dispose();

    private ICacheStore Backend(string kind) => kind == "memory" ? new MemoryCacheStore("memory", _memory) : new CacheFake();

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task Flushing_one_scope_keeps_every_other_scope_and_the_unscoped_key(string backend)
    {
        var store = Backend(backend);
        var a = new ScopedCacheStore(store, "app:user:1");
        var b = new ScopedCacheStore(store, "app:user:2");
        await store.SetAsync("plain", "v", null);
        await a.SetAsync("profile", "v", null);
        await a.SetAsync("expiring", "v", TimeSpan.FromMinutes(5));
        await a.IncrementAsync("visits", 1);
        await b.SetAsync("profile", "v", null);

        await a.FlushAsync();

        (await a.ExistsAsync("profile")).Should().BeFalse();
        (await a.ExistsAsync("expiring")).Should().BeFalse();
        (await a.ExistsAsync("visits")).Should().BeFalse();
        (await b.ExistsAsync("profile")).Should().BeTrue("another scope must survive");
        (await store.ExistsAsync("plain")).Should().BeTrue("a key outside every scope must survive");
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task A_scope_also_covers_the_scopes_nested_under_it(string backend)
    {
        var store = Backend(backend);
        var parent = new ScopedCacheStore(store, "app:tenant:a");
        var child = new ScopedCacheStore(store, "app:tenant:a:sub");
        var other = new ScopedCacheStore(store, "app:tenant:b");
        await parent.SetAsync("k", "v", null);
        await child.SetAsync("k", "v", null);
        await other.SetAsync("k", "v", null);

        await parent.FlushAsync();

        (await parent.ExistsAsync("k")).Should().BeFalse();
        (await child.ExistsAsync("k")).Should().BeFalse("the prefix of the nested scope starts with the prefix of its parent");
        (await other.ExistsAsync("k")).Should().BeTrue();
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task A_scope_does_not_reach_a_sibling_whose_name_only_starts_the_same_way(string backend)
    {
        var store = Backend(backend);
        var one = new ScopedCacheStore(store, "app:user:1");
        var ten = new ScopedCacheStore(store, "app:user:10");
        await one.SetAsync("k", "v", null);
        await ten.SetAsync("k", "v", null);

        await one.FlushAsync();

        (await one.ExistsAsync("k")).Should().BeFalse();
        (await ten.ExistsAsync("k")).Should().BeTrue("the colon after the scope name ends the prefix");
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task FlushPrefixAsync_on_a_scope_removes_only_the_keys_under_that_prefix_inside_the_scope(string backend)
    {
        var store = Backend(backend);
        var scope = new ScopedCacheStore(store, "app:user:1");
        await scope.SetAsync("orders:1", "v", null);
        await scope.SetAsync("orders:2", "v", null);
        await scope.SetAsync("profile", "v", null);

        await scope.FlushPrefixAsync("orders:");

        (await scope.ExistsAsync("orders:1")).Should().BeFalse();
        (await scope.ExistsAsync("orders:2")).Should().BeFalse();
        (await scope.ExistsAsync("profile")).Should().BeTrue();
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task A_prefix_that_is_null_empty_or_white_space_is_rejected_and_removes_nothing(string backend)
    {
        var store = Backend(backend);
        await store.SetAsync("app:keep", "v", null);
        IPrefixFlushableCacheStore[] targets = [(IPrefixFlushableCacheStore)store, new ScopedCacheStore(store, "app")];

        foreach (var target in targets)
        {
            foreach (var prefix in new[] { null!, string.Empty, " " })
            {
                Func<Task> flush = async () => await target.FlushPrefixAsync(prefix);
                await flush.Should().ThrowAsync<ArgumentException>($"'{prefix}' on {target.GetType().Name}");
            }
        }

        (await store.ExistsAsync("app:keep")).Should().BeTrue();
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("fake")]
    public async Task A_cancelled_flush_throws_and_removes_nothing(string backend)
    {
        var store = Backend(backend);
        var scope = new ScopedCacheStore(store, "app:user:1");
        await scope.SetAsync("k", "v", null);

        Func<Task> flush = async () => await scope.FlushAsync(new CancellationToken(canceled: true));

        await flush.Should().ThrowAsync<OperationCanceledException>();
        (await scope.ExistsAsync("k")).Should().BeTrue();
    }

    [Fact]
    public async Task A_store_that_cannot_remove_by_prefix_makes_the_scope_refuse_and_nothing_is_removed()
    {
        var inner = new MemoryCacheStore("plain", _memory);
        var scope = new ScopedCacheStore(new WithoutPrefixFlush(inner), "app:user:1");
        await scope.SetAsync("k", "v", null);
        await inner.SetAsync("other", "v", null);

        foreach (Func<Task> flush in new Func<Task>[] { async () => await scope.FlushAsync(), async () => await scope.FlushPrefixAsync("k") })
        {
            var error = (await flush.Should().ThrowExactlyAsync<NotSupportedException>()).Which;
            error.Message.Should().Contain("'plain'").And.Contain("Tags(");
        }

        (await scope.ExistsAsync("k")).Should().BeTrue();
        (await inner.ExistsAsync("other")).Should().BeTrue();
    }

    [Fact]
    public async Task The_manager_scopes_flush_the_scope_and_the_default_store_flushes_only_the_application_prefix()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "primary",
            ["Cache:AppPrefix"] = "myapp",
            ["Cache:Stores:primary:Driver"] = "memory",
        }).Build();
        var services = new ServiceCollection();
        services.AddNaravelCache(configuration);
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<CacheManager>();
        var driver = manager.Driver();
        await driver.SetAsync("written-outside-the-application-prefix", "v", null);
        await manager.Store().SetAsync("settings", "v", null);
        await manager.ForUser(1).SetAsync("cart", "v", null);
        await manager.ForUser(2).SetAsync("cart", "v", null);
        await manager.Scope("tenant:a").SetAsync("cart", "v", null);

        await manager.ForUser(1).FlushAsync();

        (await manager.ForUser(1).ExistsAsync("cart")).Should().BeFalse();
        (await manager.ForUser(2).ExistsAsync("cart")).Should().BeTrue();
        (await manager.Scope("tenant:a").ExistsAsync("cart")).Should().BeTrue();
        (await manager.Store().ExistsAsync("settings")).Should().BeTrue();

        await manager.Store().FlushAsync();

        (await manager.Store().ExistsAsync("settings")).Should().BeFalse();
        (await manager.ForUser(2).ExistsAsync("cart")).Should().BeFalse("every scope sits under the application prefix");
        (await manager.Scope("tenant:a").ExistsAsync("cart")).Should().BeFalse();
        (await driver.ExistsAsync("written-outside-the-application-prefix")).Should().BeTrue("the application store flush stops at the application prefix");
    }

    // A store that does not implement IPrefixFlushableCacheStore, standing for a provider that cannot remove by prefix.
    private sealed class WithoutPrefixFlush(ICacheStore inner) : ICacheStore
    {
        public string Name => inner.Name;
        public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) => inner.TryGetAsync<T>(key, cancellationToken);
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default) => inner.SetAsync(key, value, ttl, cancellationToken);
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => inner.RemoveAsync(key, cancellationToken);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
        public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default) => inner.IncrementAsync(key, by, cancellationToken);
        public Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default) => inner.IncrementAsync(key, by, ttl, cancellationToken);
        public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) => inner.DecrementAsync(key, by, cancellationToken);
        public Task FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
    }
}

// The same behaviour against a real Redis server (CI "Provider integration tests" job; skipped unless NARAVEL_TEST_REDIS is set).
public sealed class ScopedFlushRedisServerTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_scope_flush_removes_only_that_scope_and_its_nested_scopes()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var prefix = "naravel-test:" + Guid.NewGuid().ToString("N");
        using var store = new RedisCacheStore("flush", multiplexer, prefix, ownsConnection: false);
        var a = new ScopedCacheStore(store, "app:tenant:a");
        var nested = new ScopedCacheStore(store, "app:tenant:a:sub");
        var b = new ScopedCacheStore(store, "app:tenant:b");
        var star = new ScopedCacheStore(store, "app:te*nt");
        var lookalike = new ScopedCacheStore(store, "app:teXnt");
        await Task.WhenAll(Enumerable.Range(0, 700).Select(i => a.SetAsync($"key-{i}", i, null)));
        await nested.SetAsync("k", "v", null);
        await b.SetAsync("k", "v", null);
        await star.SetAsync("k", "v", null);
        await lookalike.SetAsync("k", "v", null);
        await store.SetAsync("plain", "v", null);

        await a.FlushAsync();

        (await a.ExistsAsync("key-0")).Should().BeFalse();
        (await a.ExistsAsync("key-699")).Should().BeFalse("every batch of deletes must run");
        (await nested.ExistsAsync("k")).Should().BeFalse("a nested scope sits under its parent");
        (await b.ExistsAsync("k")).Should().BeTrue();
        (await lookalike.ExistsAsync("k")).Should().BeTrue();
        (await store.ExistsAsync("plain")).Should().BeTrue();

        await star.FlushAsync();

        (await star.ExistsAsync("k")).Should().BeFalse();
        (await lookalike.ExistsAsync("k")).Should().BeTrue("a * in a scope name is not a wildcard");

        await store.FlushAsync();

        (await b.ExistsAsync("k")).Should().BeFalse();
        (await lookalike.ExistsAsync("k")).Should().BeFalse();
        (await store.ExistsAsync("plain")).Should().BeFalse();
    }
}

// A flush on Memcached is refused instead of clearing the shared cluster (CI "Provider integration tests" job; skipped unless NARAVEL_TEST_MEMCACHED is set).
public sealed class ScopedFlushMemcachedServerTests
{
    // MCD-006: the raw driver, which has no scope around it, used to call flush_all.
    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task The_raw_store_flush_is_refused_and_no_key_of_the_cluster_is_removed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memcached",
            ["Cache:AppPrefix"] = "naravel-test:" + Guid.NewGuid().ToString("N"),
            ["Cache:Stores:memcached:Driver"] = "memcached",
            ["Cache:Stores:memcached:Servers"] = Environment.GetEnvironmentVariable("NARAVEL_TEST_MEMCACHED")!,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration).AddNaravelMemcachedCache(configuration);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMemcachedClient>();
        var raw = new MemcachedCacheStore("raw", client, "naravel-test:" + Guid.NewGuid().ToString("N"));
        var neighbour = new MemcachedCacheStore("neighbour", client, "naravel-test:" + Guid.NewGuid().ToString("N"));
        await raw.SetAsync("k", "v", TimeSpan.FromMinutes(1));
        await neighbour.SetAsync("k", "v", TimeSpan.FromMinutes(1));

        Func<Task> flush = async () => await raw.FlushAsync();

        await flush.Should().ThrowExactlyAsync<NotSupportedException>();
        (await raw.ExistsAsync("k")).Should().BeTrue();
        (await neighbour.ExistsAsync("k")).Should().BeTrue();
    }

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task A_scope_flush_is_refused_and_no_key_of_the_cluster_is_removed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memcached",
            ["Cache:AppPrefix"] = "naravel-test:" + Guid.NewGuid().ToString("N"),
            ["Cache:Stores:memcached:Driver"] = "memcached",
            ["Cache:Stores:memcached:Servers"] = Environment.GetEnvironmentVariable("NARAVEL_TEST_MEMCACHED")!,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration).AddNaravelMemcachedCache(configuration);
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<CacheManager>();
        var a = manager.ForUser(1, "memcached");
        var b = manager.ForUser(2, "memcached");
        await a.SetAsync("k", "v", TimeSpan.FromMinutes(1));
        await b.SetAsync("k", "v", TimeSpan.FromMinutes(1));

        Func<Task> flushScope = async () => await a.FlushAsync();
        Func<Task> flushStore = async () => await manager.Store("memcached").FlushAsync();

        await flushScope.Should().ThrowExactlyAsync<NotSupportedException>();
        await flushStore.Should().ThrowExactlyAsync<NotSupportedException>();
        (await a.ExistsAsync("k")).Should().BeTrue();
        (await b.ExistsAsync("k")).Should().BeTrue();
    }
}
