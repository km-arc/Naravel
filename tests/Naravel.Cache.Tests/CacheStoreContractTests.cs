using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Memcached;
using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

public abstract class CacheStoreContractTests
{
    protected static async Task AssertBasicOperationsAsync(ICacheStore store)
    {
        var prefix = "contract:" + Guid.NewGuid().ToString("N") + ":";
        var valueKey = prefix + "value";
        await store.SetAsync(valueKey, "naravel", TimeSpan.FromMinutes(1));
        (await store.TryGetAsync<string>(valueKey)).Should().Be((true, "naravel"));
        (await store.ExistsAsync(valueKey)).Should().BeTrue();
        (await store.RemoveAsync(valueKey)).Should().BeTrue();
        (await store.ExistsAsync(valueKey)).Should().BeFalse();

        var counterKey = prefix + "counter";
        await store.SetAsync(counterKey, 41L, TimeSpan.FromMinutes(1));
        (await store.IncrementAsync(counterKey, 1)).Should().Be(42);
        (await store.TryGetAsync<long>(counterKey)).Value.Should().Be(42);
        await store.RemoveAsync(counterKey);
    }
}

public sealed class MemoryCacheStoreContractTests : CacheStoreContractTests
{
    [Fact]
    public async Task Shared_contract()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        await AssertBasicOperationsAsync(new Naravel.Cache.Stores.MemoryCacheStore("contract", cache));
    }
}

public sealed class RedisCacheStoreContractTests : CacheStoreContractTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Shared_contract()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        using var store = new RedisCacheStore("contract", multiplexer, "naravel-test:" + Guid.NewGuid().ToString("N"));
        await AssertBasicOperationsAsync(store);
    }
}

public sealed class MemcachedCacheStoreContractTests : CacheStoreContractTests
{
    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task Shared_contract()
    {
        var endpoint = Environment.GetEnvironmentVariable("NARAVEL_TEST_MEMCACHED")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memcached",
            ["Cache:AppPrefix"] = "naravel-test:" + Guid.NewGuid().ToString("N"),
            ["Cache:Stores:memcached:Driver"] = "memcached",
            ["Cache:Stores:memcached:Servers"] = endpoint,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration).AddNaravelMemcachedCache(configuration);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<CacheManager>().Store("memcached");
        await AssertBasicOperationsAsync(store);
    }
}