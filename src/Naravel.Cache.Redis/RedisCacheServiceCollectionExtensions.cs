using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Extensions;
using StackExchange.Redis;

namespace Naravel.Cache.Redis;

/// <summary>Registration of Redis cache and lock stores.</summary>
public static class RedisCacheServiceCollectionExtensions
{
    /// <summary>Registers every Redis store configured under the Cache section.</summary>
    public static IServiceCollection AddNaravelRedisCache(this IServiceCollection services, IConfiguration configuration, string sectionName = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        return services.AddCacheDriver(configuration, "redis",
            (_, store) => new RedisCacheStore(
                store.Key,
                ConnectionMultiplexer.Connect(store["ConnectionString"] ?? throw new InvalidOperationException($"Redis store '{store.Key}' requires ConnectionString.")),
                store["Prefix"] ?? store.Key,
                int.TryParse(store["Database"], out var database) ? database : -1),
            (_, store) => new RedisLock(
                ConnectionMultiplexer.Connect(store["ConnectionString"] ?? throw new InvalidOperationException($"Redis store '{store.Key}' requires ConnectionString.")),
                store["Prefix"] ?? store.Key,
                int.TryParse(store["Database"], out var database) ? database : -1),
            sectionName);
    }
}
