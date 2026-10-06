using Naravel.Cache.Abstractions;

namespace Naravel.Cache.Tagging;

/// <summary>Invalidates keys through per-tag version counters instead of scanning the backing store.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Cache::tags(...)</c>; works on stores without pattern-delete support.</remarks>
public sealed class TaggedCacheStore : ICacheStore
{
    private const string TagVersionPrefix = "__naravel_tag_version:";
    private readonly ICacheStore _inner;
    private readonly string[] _tags;

    /// <summary>Creates a tagged view over an existing store or scope.</summary>
    public TaggedCacheStore(ICacheStore inner, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(tags);
        _inner = inner;
        _tags = tags.Select(tag => tag?.Trim()).Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()!;
        if (_tags.Length == 0) throw new ArgumentException("At least one non-empty cache tag is required.", nameof(tags));
    }

    /// <inheritdoc />
    public string Name => _inner.Name;

    private async Task<string> BuildKeyAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var versions = new string[_tags.Length];
        for (var index = 0; index < _tags.Length; index++)
        {
            var versionKey = TagVersionPrefix + _tags[index];
            var (found, version) = await _inner.TryGetAsync<long>(versionKey, cancellationToken).ConfigureAwait(false);
            var current = found ? version : 0L;
            versions[index] = $"{_tags[index]}@{current}";
        }

        return string.Join(':', versions) + ":" + key;
    }

    /// <inheritdoc />
    public async Task<(bool Found, T? Value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        await _inner.TryGetAsync<T>(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl, CancellationToken cancellationToken = default) =>
        await _inner.SetAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), value, ttl, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        await _inner.RemoveAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        await _inner.ExistsAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        await _inner.IncrementAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), by, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> IncrementAsync(string key, long by, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        await _inner.IncrementAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), by, ttl, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<long> DecrementAsync(string key, long by, CancellationToken cancellationToken = default) =>
        await _inner.DecrementAsync(await BuildKeyAsync(key, cancellationToken).ConfigureAwait(false), by, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        foreach (var tag in _tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _inner.IncrementAsync(TagVersionPrefix + tag, 1, cancellationToken).ConfigureAwait(false);
        }
    }
}
