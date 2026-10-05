using Microsoft.Extensions.Options;
using Naravel.Cache.Abstractions;
using Naravel.Foundation;

namespace Naravel.Cache;

/// <summary>Resolves named cache lock drivers.</summary>
/// <remarks><b>Laravel equivalent:</b> the lock operations exposed by Laravel's cache manager.</remarks>
public sealed class LockManager(
    IServiceProvider provider,
    IDriverRegistry<ICacheLock> registry,
    IOptionsMonitor<CacheOptions> options)
    : Manager<ICacheLock, CacheOptions>(provider, registry, options);
