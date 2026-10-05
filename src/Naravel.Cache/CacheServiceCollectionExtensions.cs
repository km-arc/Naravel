using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Caching.Memory;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Extensions;
using Naravel.Cache.Locks;
using Naravel.Cache.Stores;
using Naravel.Foundation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the cache and lock managers with the built-in memory providers.</summary>
public static class CacheServiceCollectionExtensions
{
    /// <summary>Registers cache and lock managers from a configuration section.</summary>
    public static IServiceCollection AddNaravelCache(this IServiceCollection services, IConfiguration configuration, string sectionName = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddMemoryCache();
        services.AddNaravelDriver<ICacheStore>("memory", provider => new MemoryCacheStore(
            "memory", provider.GetRequiredService<IMemoryCache>()));
        services.AddNaravelDriver<ICacheLock>("memory", _ => new MemoryLock("memory"));
        services.AddCacheDriver(configuration, "memory",
            (provider, store) => new MemoryCacheStore(
                store.Key,
                provider.GetRequiredService<IMemoryCache>(),
                store["Prefix"] ?? store.Key),
            (_, store) => new MemoryLock(store["LockPrefix"] ?? store["Prefix"] ?? store.Key),
            sectionName);

        services.TryAddSingleton<ICacheStore>(provider => provider.GetRequiredService<CacheManager>().Store());
        services.TryAddSingleton<ICacheLock>(provider => provider.GetRequiredService<LockManager>().Driver());
        var section = configuration.GetSection(sectionName);
        services.AddNaravelManager<CacheManager, ICacheStore, CacheOptions>(section);
        services.AddNaravelManager<LockManager, ICacheLock, CacheOptions>(section);
        return services;
    }
}
