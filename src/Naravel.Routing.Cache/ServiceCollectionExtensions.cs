using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Cache;
using Naravel.Cache.Abstractions;
using Naravel.Foundation;
using Naravel.Routing;
using Naravel.Routing.Cache;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of the shared store for the route <c>throttle</c> middleware.</summary>
public static class NaravelRoutingCacheServiceCollectionExtensions
{
    /// <summary>
    /// Makes the <c>throttle</c> middleware count in the named <c>Naravel.Cache</c> store, so every application instance that uses
    /// the same store shares one quota per policy, subject and window.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="storeName">
    /// A store configured under the Cache section (for example <c>redis</c>). It must be shared between instances to be useful: the
    /// <c>memory</c> store counts per process, exactly like the default, and is meant for development and tests.
    /// </param>
    /// <returns>The service collection.</returns>
    /// <remarks>
    /// <para>
    /// <b>Laravel equivalent:</b> the cache store that Laravel's <c>throttle</c> middleware counts in. <b>Why it exists:</b> the
    /// default store counts in the memory of one process, so N instances allow N times the limit (PDR-024). Policies, the
    /// <c>throttle:login</c> alias, the headers and the 429 response are unchanged.
    /// </para>
    /// <para>
    /// Call it next to <c>AddNaravelRouting</c> and <c>AddNaravelCache</c>, in either order; calling it again replaces the earlier
    /// registration (the last call wins). The store name is checked when the throttle is first used, and an unknown name throws
    /// <see cref="DriverNotRegisteredException"/> with the names that exist. A backend that is down or slow is handled by the throttle
    /// itself according to each policy's failure mode (<see cref="ThrottleFailureMode"/>), not by this method.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="storeName"/> is empty or white space.</exception>
    public static IServiceCollection AddNaravelSharedThrottle(this IServiceCollection services, string storeName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeName);

        services.Replace(ServiceDescriptor.Singleton<IThrottleStore>(provider =>
        {
            var caches = provider.GetRequiredService<CacheManager>();
            var registry = provider.GetRequiredService<IDriverRegistry<ICacheStore>>();
            if (!registry.IsRegistered(storeName))
            {
                throw new DriverNotRegisteredException(storeName, nameof(ICacheStore), registry.Names);
            }

            return new CacheThrottleStore(caches, storeName, provider.GetService<TimeProvider>() ?? TimeProvider.System);
        }));
        return services;
    }
}
