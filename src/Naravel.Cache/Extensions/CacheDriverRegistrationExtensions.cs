using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Foundation;

namespace Naravel.Cache.Extensions;

/// <summary>Registers cache and lock factories for configured stores handled by one provider package.</summary>
public static class CacheDriverRegistrationExtensions
{
    /// <summary>Registers matching named stores using the supplied cache and lock factories.</summary>
    public static IServiceCollection AddCacheDriver(
        this IServiceCollection services,
        IConfiguration configuration,
        string driverName,
        Func<IServiceProvider, IConfigurationSection, ICacheStore> cacheFactory,
        Func<IServiceProvider, IConfigurationSection, ICacheLock> lockFactory,
        string sectionName = "Cache")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(driverName);
        ArgumentNullException.ThrowIfNull(cacheFactory);
        ArgumentNullException.ThrowIfNull(lockFactory);

        foreach (var store in configuration.GetSection($"{sectionName}:Stores").GetChildren())
        {
            if (!string.Equals(store["Driver"], driverName, StringComparison.OrdinalIgnoreCase)) continue;
            var storeSection = store;
            services.AddNaravelDriver<ICacheStore>(store.Key, provider => cacheFactory(provider, storeSection));
            services.AddNaravelDriver<ICacheLock>(store.Key, provider => lockFactory(provider, storeSection));
        }

        return services;
    }
}