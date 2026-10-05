using System.Collections.Concurrent;

namespace Naravel.Foundation;

/// <summary>Default thread-safe implementation of <see cref="IDriverRegistry{TDriver}"/>.</summary>
/// <typeparam name="TDriver">The driver contract.</typeparam>
/// <remarks>See <see cref="IDriverRegistry{TDriver}"/> for the Laravel parity notes.</remarks>
public sealed class DriverRegistry<TDriver> : IDriverRegistry<TDriver> where TDriver : notnull
{
    private sealed record Entry(
        Func<IServiceProvider, TDriver>? Sync,
        Func<IServiceProvider, CancellationToken, ValueTask<TDriver>>? Async);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event Action<string>? Registered;

    /// <inheritdoc />
    public IReadOnlyCollection<string> Names => _entries.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <inheritdoc />
    public void Register(string name, Func<IServiceProvider, TDriver> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _entries[name] = new Entry(factory, null);
        Registered?.Invoke(name);
    }

    /// <inheritdoc />
    public void Register(string name, Func<IServiceProvider, CancellationToken, ValueTask<TDriver>> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _entries[name] = new Entry(null, factory);
        Registered?.Invoke(name);
    }

    /// <inheritdoc />
    public bool IsRegistered(string name) => name is not null && _entries.ContainsKey(name);

    /// <inheritdoc />
    public bool IsAsyncOnly(string name) =>
        name is not null && _entries.TryGetValue(name, out var entry) && entry.Sync is null;

    /// <inheritdoc />
    public TDriver Create(string name, IServiceProvider provider)
    {
        var entry = GetEntry(name);
        if (entry.Sync is null)
        {
            throw new InvalidOperationException(
                $"Driver '{name}' was registered with an asynchronous factory and cannot be created synchronously. " +
                "Use CreateAsync (or Manager.DriverAsync).");
        }

        return entry.Sync(provider);
    }

    /// <inheritdoc />
    public async ValueTask<TDriver> CreateAsync(string name, IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        var entry = GetEntry(name);
        if (entry.Async is not null)
        {
            return await entry.Async(provider, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return entry.Sync!(provider);
    }

    private Entry GetEntry(string name)
    {
        if (name is not null && _entries.TryGetValue(name, out var entry))
        {
            return entry;
        }

        throw new DriverNotRegisteredException(name ?? "(null)", typeof(TDriver).Name, Names);
    }
}
