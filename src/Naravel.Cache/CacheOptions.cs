using Naravel.Foundation;

namespace Naravel.Cache;

/// <summary>Configuration for named cache and lock stores.</summary>
/// <remarks><b>Laravel equivalent:</b> the cache configuration's default store and store list.</remarks>
public sealed class CacheOptions : ManagerOptions
{
    /// <summary>Application prefix applied by <see cref="CacheManager"/> to returned stores.</summary>
    public string AppPrefix { get; set; } = "app";

    /// <summary>Creates options with the memory store as the default.</summary>
    public CacheOptions() => Default = "memory";
}
