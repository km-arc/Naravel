using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Tagging;

/// <summary>Fluent tag decorators for cache stores.</summary>
public static class TaggingExtensions
{
    /// <summary>Creates a cache view invalidated when any supplied tag is flushed.</summary>
    public static ICacheStore Tags(this ICacheStore store, params string[] tags) => new TaggedCacheStore(store, tags);

    /// <summary>Creates a cache view from an enumerable tag collection.</summary>
    public static ICacheStore Tags(this ICacheStore store, IEnumerable<string> tags) => new TaggedCacheStore(store, tags);
}
