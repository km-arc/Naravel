using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Cache.RateLimiting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the Cache-backed rate limiter against a default or named Cache store.</summary>
public static class RateLimiterServiceCollectionExtensions
{
    /// <summary>Registers the rate limiter and selects its backing named store.</summary>
    public static IServiceCollection AddNaravelRateLimiter(this IServiceCollection services, string? storeName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (storeName is not null) ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        services.TryAddSingleton<IRateLimiter>(provider => new CacheRateLimiter(
            provider.GetRequiredService<Naravel.Cache.CacheManager>(),
            provider.GetRequiredService<Naravel.Cache.LockManager>(),
            storeName));
        return services;
    }
}
