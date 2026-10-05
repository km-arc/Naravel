using Microsoft.Extensions.Options;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Scoping;
using Naravel.Cache.Tagging;
using Naravel.Foundation;

namespace Naravel.Cache;

/// <summary>Resolves named cache stores and creates scoped/tagged views.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Cache\CacheManager</c>.</remarks>
public sealed class CacheManager(
    IServiceProvider provider,
    IDriverRegistry<ICacheStore> registry,
    IOptionsMonitor<CacheOptions> options)
    : Manager<ICacheStore, CacheOptions>(provider, registry, options)
{
    private readonly IOptionsMonitor<CacheOptions> _options = options;

    /// <summary>Gets the application-scoped default or named cache store.</summary>
    public ICacheStore Store(string? name = null) => new ScopedCacheStore(Driver(name), _options.CurrentValue.AppPrefix);

    /// <summary>Gets a cache store scoped to one user identifier.</summary>
    public ICacheStore ForUser(object userId, string? storeName = null)
    {
        ArgumentNullException.ThrowIfNull(userId);
        return new ScopedCacheStore(Driver(storeName), $"{_options.CurrentValue.AppPrefix}:user:{userId}");
    }

    /// <summary>Gets a cache store scoped to an explicit namespace such as a tenant.</summary>
    public ICacheStore Scope(string scope, string? storeName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        return new ScopedCacheStore(Driver(storeName), $"{_options.CurrentValue.AppPrefix}:{scope}");
    }

    /// <summary>Gets an application-scoped cache store invalidated by the supplied tags.</summary>
    public ICacheStore Tags(params string[] tags) => Store().Tags(tags);

    /// <summary>Gets a named application-scoped cache store invalidated by the supplied tags.</summary>
    public ICacheStore TagsOn(string? storeName, params string[] tags) => Store(storeName).Tags(tags);

    /// <summary>Invalidates entries carrying the given tag in the selected store.</summary>
    public Task FlushTagAsync(string tag, string? storeName = null, CancellationToken cancellationToken = default) =>
        Store(storeName).Tags(tag).FlushAsync(cancellationToken);
}
