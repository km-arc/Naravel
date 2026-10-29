using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Extensions;
using Naravel.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Registration of Redis cache and lock stores.</summary>
public static class RedisCacheServiceCollectionExtensions
{
    /// <summary>The key of the optional <see cref="System.Text.Json.JsonSerializerOptions"/> that every Redis cache store uses for its JSON values (keyed service; register it for trimming or Native AOT).</summary>
    public const string SerializerOptionsKey = "Naravel.Cache.Redis.Json";

    /// <summary>
    /// Registers every Redis store configured under the Cache section. A store names a shared connection with <c>Connection</c> (a <c>Redis:Stores</c> name) or, as before,
    /// carries its own <c>ConnectionString</c>; equal strings share one connection. The store and its lock use the same connection, which <c>Naravel.Redis</c> owns.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration (also read for the <c>Redis</c> section).</param>
    /// <param name="sectionName">The cache section, <c>Cache</c> by default.</param>
    public static IServiceCollection AddNaravelRedisCache(this IServiceCollection services, IConfiguration configuration, string sectionName = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddNaravelRedis(configuration);
        return services.AddCacheDriver(configuration, "redis",
            (provider, store) => new RedisCacheStore(
                store.Key,
                Resolve(provider, store).Multiplexer,
                store["Prefix"] ?? store.Key,
                int.TryParse(store["Database"], out var database) ? database : -1,
                ownsConnection: false,
                serializerOptions: provider.GetKeyedService<System.Text.Json.JsonSerializerOptions>(SerializerOptionsKey)),
            (provider, store) => new RedisLock(
                Resolve(provider, store).Multiplexer,
                store["Prefix"] ?? store.Key,
                int.TryParse(store["Database"], out var database) ? database : -1,
                ownsConnection: false),
            sectionName);
    }

    private static IRedisConnection Resolve(IServiceProvider provider, IConfigurationSection store)
    {
        var redis = provider.GetRequiredService<RedisManager>();
        if (store["Connection"] is { Length: > 0 } name)
        {
            return redis.Connection(name);
        }

        return redis.FromConnectionString(store["ConnectionString"] ?? throw new InvalidOperationException($"Redis store '{store.Key}' requires Connection or ConnectionString."));
    }
}
