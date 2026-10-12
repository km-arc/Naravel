using FluentAssertions.Execution;
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

        var expiringCounterKey = prefix + "expiring-counter";
        var ttl = TimeSpan.FromSeconds(3);
        (await store.IncrementAsync(expiringCounterKey, 1, ttl)).Should().Be(1);
        var increments = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => store.IncrementAsync(expiringCounterKey, 1, ttl)));
        increments.Distinct().Should().HaveCount(16);
        increments.Max().Should().Be(17);

        await Task.Delay(TimeSpan.FromMilliseconds(1600));
        (await store.IncrementAsync(expiringCounterKey, 1, ttl)).Should().Be(18);
        await Task.Delay(TimeSpan.FromMilliseconds(1600));
        (await store.ExistsAsync(expiringCounterKey)).Should().BeFalse("later increments must not extend the original window");
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
/// <summary>
/// R07.T09: the Memcached provider goes through EnyimMemcachedCore's transcoder, which depends on the pinned
/// <c>MessagePack</c> package (see Directory.Packages.props). The shared contract above only stores strings and counters,
/// so these tests round-trip richer values against a real server. They are skipped unless NARAVEL_TEST_MEMCACHED is set
/// and run in the CI "services" job, which sets it and starts a Memcached container.
/// </summary>
public sealed class MemcachedSerializerRoundTripTests
{
    public sealed class Profile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public double Score { get; set; }
        public bool Active { get; set; }
        public DateTime Joined { get; set; }
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, int> Counters { get; set; } = new();
    }

    private static ServiceProvider BuildProvider()
    {
        var endpoint = Environment.GetEnvironmentVariable("NARAVEL_TEST_MEMCACHED")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memcached",
            ["Cache:AppPrefix"] = "naravel-serializer:" + Guid.NewGuid().ToString("N"),
            ["Cache:Stores:memcached:Driver"] = "memcached",
            ["Cache:Stores:memcached:Servers"] = endpoint,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration).AddNaravelMemcachedCache(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task CheckAsync<T>(ICacheStore store, string name, T value)
    {
        try
        {
            await store.SetAsync(name, value, TimeSpan.FromMinutes(1));
            var (found, loaded) = await store.TryGetAsync<T>(name);
            found.Should().BeTrue($"{name} must be found after it was stored");
            if (found) loaded.Should().BeEquivalentTo(value, $"{name} must round-trip unchanged");
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            Execute.Assertion.FailWith("{0} threw {1}: {2}", name, ex.GetType().Name, ex.Message);
        }
    }

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task Common_value_types_round_trip_through_the_real_server()
    {
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<CacheManager>().Store("memcached");

        // One scope: every value type that does not survive is reported together instead of stopping at the first.
        using (new AssertionScope())
        {
            await CheckAsync(store, "string", "naravel \u0646\u0627\u0631\u0627\u0648\u0644");
            await CheckAsync(store, "int", 123456);
            await CheckAsync(store, "long", 9_000_000_000L);
            await CheckAsync(store, "double", 3.14159265358979);
            await CheckAsync(store, "bool", true);
            await CheckAsync(store, "guid", Guid.NewGuid());
            await CheckAsync(store, "datetime", new DateTime(2026, 10, 12, 8, 30, 15, DateTimeKind.Utc));
            await CheckAsync(store, "datetimeoffset", new DateTimeOffset(2026, 10, 12, 8, 30, 15, TimeSpan.Zero));
            await CheckAsync(store, "decimal", 12345.6789m);
            await CheckAsync(store, "bytes", new byte[] { 0, 1, 2, 250, 255 });
            await CheckAsync(store, "strings", new[] { "a", "b", "c" });
            (await store.TryGetAsync<string>("missing")).Found.Should().BeFalse();
        }
    }

    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task A_plain_object_with_collections_round_trips_through_the_real_server()
    {
        await using var provider = BuildProvider();
        var store = provider.GetRequiredService<CacheManager>().Store("memcached");
        {
            var profile = new Profile
            {
                Id = Guid.NewGuid(),
                Name = "Naravel",
                Age = 7,
                Score = 99.5,
                Active = true,
                Joined = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
                Tags = { "queue", "cache" },
                Counters = { ["hits"] = 3, ["misses"] = 1 },
            };

            await store.SetAsync("profile", profile, TimeSpan.FromMinutes(1));
            var (found, loaded) = await store.TryGetAsync<Profile>("profile");

            found.Should().BeTrue();
            loaded.Should().BeEquivalentTo(profile);
        }
    }
}
