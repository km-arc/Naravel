namespace Naravel.Cache.Abstractions;

/// <summary>An optional capability of a cache store: remove only the entries whose key starts with a given prefix.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none. Laravel's <c>Cache::flush()</c> clears the whole store and its tags keep a list of keys; neither can clear one tenant or user.</para>
/// <para><b>Why it exists:</b> <see cref="Scoping.ScopedCacheStore"/> prefixes every key with its scope, so flushing one scope has to remove exactly the keys under that prefix. A store that
/// can do that implements this interface; a store that cannot does not, and the scoped view then refuses to flush instead of clearing more than the caller asked for.</para>
/// <para><b>Contract:</b> the prefix is relative to the store, in the same key space as the keys passed to <see cref="ICacheStore.SetAsync{T}"/>; the store's own configured prefix is applied
/// by the store. The call is complete-or-fail: it throws when it cannot reach every place that may hold a matching key. It is not atomic, so after a failure some matching keys may be
/// gone and the call can simply be repeated, and a key written while the call runs may survive it.</para>
/// <para><b>Not ported on purpose:</b> a per-tag or per-scope key index. Maintaining one costs an extra write on every <c>SetAsync</c> for an operation that is rare; use
/// <see cref="Tagging.TaggingExtensions"/> to invalidate a group of keys on a hot path.</para>
/// <para><b>Decorators:</b> a type that wraps an <see cref="ICacheStore"/> and should keep this capability has to implement the interface and forward to the store it wraps.</para>
/// </remarks>
public interface IPrefixFlushableCacheStore
{
    /// <summary>Removes every entry whose key starts with <paramref name="keyPrefix"/>.</summary>
    /// <param name="keyPrefix">The key prefix, relative to the store. It is matched literally, so characters such as <c>*</c> and <c>?</c> have no special meaning.</param>
    /// <param name="cancellationToken">Cancels the call; entries already removed stay removed.</param>
    /// <exception cref="ArgumentException"><paramref name="keyPrefix"/> is <see langword="null"/>, empty or white space. An empty prefix would match every key, which is what <see cref="ICacheStore.FlushAsync"/> is for.</exception>
    Task FlushPrefixAsync(string keyPrefix, CancellationToken cancellationToken = default);
}
