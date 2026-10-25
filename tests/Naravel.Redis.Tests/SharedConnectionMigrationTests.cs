using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Redis;
using Naravel.Foundation;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Redis;
using StackExchange.Redis;

namespace Naravel.Redis.Tests;

// No Redis server: the opener hands out counting stand-ins (see RedisManagerTests), so these tests count connections and disposals only.
public sealed class SharedConnectionMigrationTests
{
    private static (ServiceProvider Provider, Opener Opener) Build(Dictionary<string, string?> settings, Action<IServiceCollection, IConfiguration>? register = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var opener = new Opener();
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IConnectionMultiplexer>>(opener.Open);
        register?.Invoke(services, configuration);
        return (services.BuildServiceProvider(), opener);
    }

    private static ICacheStore Store(ServiceProvider provider, string name) => provider.GetRequiredService<IDriverRegistry<ICacheStore>>().Create(name, provider);

    private static ICacheLock Lock(ServiceProvider provider, string name) => provider.GetRequiredService<IDriverRegistry<ICacheLock>>().Create(name, provider);

    [Fact]
    public void A_cache_store_and_its_lock_with_one_connection_string_open_one_connection()
    {
        var (provider, opener) = Build(
            new() { ["Cache:Stores:app:Driver"] = "redis", ["Cache:Stores:app:ConnectionString"] = "host:6379" },
            (services, configuration) =>
            {
                services.AddNaravelCache(configuration);
                services.AddNaravelRedisCache(configuration);
            });

        Store(provider, "app");
        Lock(provider, "app");

        opener.Opened.Should().ContainSingle().Which.Should().Be("host:6379");
    }

    [Fact]
    public void Two_cache_stores_with_the_same_connection_string_share_it()
    {
        var (provider, opener) = Build(
            new()
            {
                ["Cache:Stores:one:Driver"] = "redis", ["Cache:Stores:one:ConnectionString"] = "host:6379", ["Cache:Stores:one:Prefix"] = "a",
                ["Cache:Stores:two:Driver"] = "redis", ["Cache:Stores:two:ConnectionString"] = "host:6379", ["Cache:Stores:two:Prefix"] = "b"
            },
            (services, configuration) =>
            {
                services.AddNaravelCache(configuration);
                services.AddNaravelRedisCache(configuration);
            });

        Store(provider, "one");
        Store(provider, "two");

        opener.Opened.Should().ContainSingle();
    }

    [Fact]
    public void A_cache_store_can_name_a_shared_connection_and_a_queue_store_reuses_it()
    {
        var (provider, opener) = Build(
            new()
            {
                ["Redis:Default"] = "main",
                ["Redis:Stores:main:ConnectionString"] = "main-host:6379",
                ["Cache:Stores:app:Driver"] = "redis", ["Cache:Stores:app:Connection"] = "main",
                ["NaravelQueue:Stores:jobs:Driver"] = "redis", ["NaravelQueue:Stores:jobs:Connection"] = "main"
            },
            (services, configuration) =>
            {
                services.AddNaravelCache(configuration);
                services.AddNaravelRedisCache(configuration);
                services.AddQueue(configuration);
                services.AddRedisDriver(configuration);
            });

        Store(provider, "app");
        provider.GetRequiredService<IDriverRegistry<IQueueDriver>>().Create("jobs", provider).Should().BeOfType<RedisQueueDriver>();

        opener.Opened.Should().Equal("main-host:6379");
    }

    [Fact]
    public void Disposing_a_borrowed_store_does_not_close_the_shared_connection_but_the_manager_does_once()
    {
        var (provider, opener) = Build(
            new() { ["Cache:Stores:app:Driver"] = "redis", ["Cache:Stores:app:ConnectionString"] = "host:6379" },
            (services, configuration) =>
            {
                services.AddNaravelCache(configuration);
                services.AddNaravelRedisCache(configuration);
            });
        ((IDisposable)Store(provider, "app")).Dispose();
        ((IDisposable)Lock(provider, "app")).Dispose();

        opener.Created.Should().ContainSingle().Which.Disposed.Should().Be(0);
        provider.Dispose();

        opener.Created.Single().Disposed.Should().Be(1);
    }

    [Fact]
    public void A_store_without_connection_or_connection_string_fails_naming_the_store()
    {
        var (provider, _) = Build(
            new() { ["Cache:Stores:app:Driver"] = "redis" },
            (services, configuration) =>
            {
                services.AddNaravelCache(configuration);
                services.AddNaravelRedisCache(configuration);
            });

        var act = () => Store(provider, "app");

        act.Should().Throw<InvalidOperationException>().WithMessage("*'app'*Connection*ConnectionString*");
    }

    [Fact]
    public void A_queue_driver_told_to_own_its_connection_disposes_it()
    {
        var opener = new Opener();
        var multiplexer = opener.Open("x");
        var driver = new RedisQueueDriver(multiplexer, TimeSpan.FromSeconds(1), ownsConnection: true);

        driver.Dispose();
        driver.Dispose();

        opener.Created.Single().Disposed.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void A_queue_driver_leaves_the_connection_it_was_given_open_by_default()
    {
        var opener = new Opener();
        var driver = new RedisQueueDriver(opener.Open("x"), TimeSpan.FromSeconds(1));

        driver.Dispose();

        opener.Created.Single().Disposed.Should().Be(0);
    }

    [Fact]
    public void The_public_constructors_still_dispose_the_connection_they_were_given_by_default()
    {
        var opener = new Opener();
        new RedisCacheStore("s", opener.Open("a"), "p").Dispose();
        new RedisLock(opener.Open("b"), "p").Dispose();
        new RedisCacheStore("s", opener.Open("c"), "p", ownsConnection: false).Dispose();

        opener.Created.Select(multiplexer => multiplexer.Disposed).Should().Equal(1, 1, 0);
    }
}
