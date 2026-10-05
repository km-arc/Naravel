using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Memcached;
using Naravel.Cache.Redis;
using Naravel.Foundation;

namespace Naravel.Cache.Tests;

public sealed class CacheManagerIntegrationTests
{
    [Fact]
    public async Task Same_driver_type_can_back_distinct_named_stores_and_names_are_case_insensitive()
    {
        var config = Config(
            ("Default", "primary"),
            ("Stores:primary:Driver", "memory"),
            ("Stores:secondary:Driver", "memory"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<CacheManager>();
        var primary = manager.Store("PRIMARY");
        var secondary = manager.Store("secondary");

        await primary.SetAsync("key", "one", null);

        (await secondary.TryGetAsync<string>("key")).Found.Should().BeFalse();
        (await manager.Store().TryGetAsync<string>("key")).Value.Should().Be("one");
    }

    [Fact]
    public async Task Named_memory_locks_are_isolated_without_explicit_prefixes()
    {
        var config = Config(
            ("Default", "primary"),
            ("Stores:primary:Driver", "memory"),
            ("Stores:secondary:Driver", "memory"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<LockManager>();

        var primaryToken = await manager.Driver("primary").AcquireAsync("work", TimeSpan.FromSeconds(5));
        var secondaryToken = await manager.Driver("secondary").AcquireAsync("work", TimeSpan.FromSeconds(5));

        primaryToken.Should().NotBeNull();
        secondaryToken.Should().NotBeNull();
    }

    [Fact]
    public void Runtime_extend_registers_a_custom_cache_store()
    {
        var config = Config(("Default", "custom"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<CacheManager>();
        var custom = new FakeStore("custom");

        manager.Extend("custom", _ => custom);

        manager.Driver().Should().BeSameAs(custom);
        manager.Store().Should().BeOfType<Naravel.Cache.Scoping.ScopedCacheStore>();
    }

    [Fact]
    public async Task Store_reload_rebuilds_changed_stores_but_ignores_unrelated_settings()
    {
        var config = Config(("Default", "memory"), ("Stores:memory:Driver", "memory"), ("Stores:memory:Prefix", "before"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<CacheManager>();
        var original = manager.Driver();

        config["Cache:Unrelated"] = "updated";
        ((IConfigurationRoot)config).Reload();
        manager.Driver().Should().BeSameAs(original);

        config["Cache:Stores:memory:Prefix"] = "after";
        ((IConfigurationRoot)config).Reload();
        manager.Driver().Should().NotBeSameAs(original);
        await manager.Store().SetAsync("key", 3, null);
        (await manager.Store().TryGetAsync<int>("key")).Value.Should().Be(3);
    }

    [Fact]
    public async Task Default_cache_and_lock_services_are_registered_and_lock_tokens_are_enforced()
    {
        var config = Config(("Stores:memory:Driver", "memory"));
        using var provider = Build(config);
        var store = provider.GetRequiredService<ICacheStore>();
        var cacheLock = provider.GetRequiredService<ICacheLock>();

        await store.SetAsync("direct", true, null);
        (await store.TryGetAsync<bool>("direct")).Value.Should().BeTrue();
        var token = await cacheLock.AcquireAsync("work", TimeSpan.FromSeconds(5));
        token.Should().NotBeNull();
        (await cacheLock.ReleaseAsync("work", "wrong-token")).Should().BeFalse();
        (await cacheLock.ReleaseAsync("work", token!)).Should().BeTrue();
    }

    [Fact]
    public void Lock_manager_supports_runtime_extend_and_config_reload()
    {
        var config = Config(("Default", "memory"), ("Stores:memory:Driver", "memory"), ("Stores:memory:LockPrefix", "before"));
        using var provider = Build(config);
        var manager = provider.GetRequiredService<LockManager>();
        var original = manager.Driver();
        var custom = new FakeLock();

        manager.Extend("custom", _ => custom);
        manager.Driver("custom").Should().BeSameAs(custom);

        config["Cache:Stores:memory:LockPrefix"] = "after";
        ((IConfigurationRoot)config).Reload();
        manager.Driver().Should().NotBeSameAs(original);
    }

    [Fact]
    public void Cache_manager_disposes_custom_stores_it_owns()
    {
        var config = Config(("Default", "custom"));
        var provider = Build(config);
        var manager = provider.GetRequiredService<CacheManager>();
        var custom = new DisposableFakeStore("custom");
        manager.Extend("custom", _ => custom);
        manager.Driver();

        provider.Dispose();

        custom.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Lock_manager_disposes_custom_locks_it_owns()
    {
        var config = Config(("Default", "custom"));
        var provider = Build(config);
        var manager = provider.GetRequiredService<LockManager>();
        var custom = new DisposableFakeLock();
        manager.Extend("custom", _ => custom);
        manager.Driver();

        provider.Dispose();

        custom.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void Redis_and_memcached_provider_registration_adds_configured_names_without_connecting()
    {
        var config = Config(
            ("Default", "memory"),
            ("Stores:memory:Driver", "memory"),
            ("Stores:redis-primary:Driver", "redis"),
            ("Stores:redis-primary:ConnectionString", "localhost:6379"),
            ("Stores:memcached-primary:Driver", "memcached"),
            ("Stores:memcached-primary:Servers:0:Address", "localhost"),
            ("Stores:memcached-primary:Servers:0:Port", "11211"),
            ("Stores:memcached-secondary:Driver", "memcached"),
            ("Stores:memcached-secondary:Servers:0:Address", "LOCALHOST"),
            ("Stores:memcached-secondary:Servers:0:Port", "11211"));
        var services = new ServiceCollection();
        services.AddNaravelCache(config);
        services.AddNaravelRedisCache(config);
        services.AddNaravelMemcachedCache(config);
        using var provider = services.BuildServiceProvider();
        var cacheRegistry = provider.GetRequiredService<IDriverRegistry<ICacheStore>>();
        var lockRegistry = provider.GetRequiredService<IDriverRegistry<ICacheLock>>();

        cacheRegistry.Names.Should().Contain(new[] { "memory", "redis-primary", "memcached-primary" });
        cacheRegistry.Names.Should().Contain("memcached-secondary");
        lockRegistry.Names.Should().Contain(new[] { "memory", "redis-primary", "memcached-primary" });
        lockRegistry.Names.Should().Contain("memcached-secondary");
    }

    [Fact]
    public void Memcached_provider_rejects_different_server_pools_instead_of_merging_them()
    {
        var config = Config(
            ("Default", "first"),
            ("Stores:first:Driver", "memcached"),
            ("Stores:first:Servers", "cache-a:11211"),
            ("Stores:second:Driver", "memcached"),
            ("Stores:second:Servers", "cache-b:11211"));
        var services = new ServiceCollection();
        services.AddNaravelCache(config);

        var act = () => services.AddNaravelMemcachedCache(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must use the same server list*");
    }

    private static ServiceProvider Build(ConfigurationManager configuration)
    {
        var services = new ServiceCollection();
        services.AddNaravelCache(configuration);
        return services.BuildServiceProvider();
    }

    private static ConfigurationManager Config(params (string Key, string? Value)[] values)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(values.ToDictionary(item => $"Cache:{item.Key}", item => item.Value));
        return configuration;
    }

    private sealed class FakeStore(string name) : ICacheStore
    {
        public string Name => name;
        public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult((false, default(T)));
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default) => Task.FromResult(by);
        public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) => Task.FromResult(-by);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeLock : ICacheLock
    {
        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult<string?>("token");
        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class DisposableFakeStore(string name) : ICacheStore, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public string Name => name;
        public Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult((false, default(T)));
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default) => Task.FromResult(by);
        public Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) => Task.FromResult(-by);
        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() => IsDisposed = true;
    }

    private sealed class DisposableFakeLock : ICacheLock, IDisposable
    {
        public bool IsDisposed { get; private set; }
        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult<string?>("token");
        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void Dispose() => IsDisposed = true;
    }
}
