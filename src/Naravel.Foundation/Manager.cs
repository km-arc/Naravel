using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Naravel.Foundation;

/// <summary>
/// Base class for every Naravel "manager" (CacheManager, QueueManager, MailManager, ...): resolves a named driver,
/// creates it exactly once, caches it, and rebuilds it when its configuration really changes.
/// </summary>
/// <typeparam name="TDriver">The driver contract.</typeparam>
/// <typeparam name="TOptions">The module options (typically derived from <see cref="ManagerOptions"/>).</typeparam>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Support\Manager</c> (<c>driver()</c>, <c>extend()</c>,
/// <c>forgetDrivers()</c>, <c>getDefaultDriver()</c>).</para>
/// <para><b>Why it exists:</b> Laravel's container does not solve "N named implementations of one contract,
/// chosen by string at runtime"; <c>Manager</c> does. .NET Keyed DI covers part of it but cannot add drivers after the
/// host is built and does not guarantee once-only asynchronous creation. This class fills exactly that gap and
/// leaves the DI container itself untouched (PDR-002).</para>
/// <para><b>Not ported on purpose:</b> Laravel's <c>__call</c> magic forwarding of driver methods through the manager.
/// In .NET, register the default driver directly in DI (done by <c>AddNaravelManager</c>) and inject the driver;
/// inject the manager only when you need to pick a driver at runtime (PDR-003).</para>
/// <para><b>Guarantees:</b> one creation per name even under concurrency; failures are never cached; callers can cancel
/// waiting without aborting a shared creation; the sync API never blocks on asynchronous factories.</para>
/// <para><b>Ownership:</b> the manager owns factory-created drivers except the default instance transferred to the
/// dependency-injection container when the direct <typeparamref name="TDriver"/> service registered by
/// <c>AddNaravelManager</c> is resolved. The container disposes that instance and the manager skips it, even if a
/// configuration reload later retires it. If the direct service is never resolved, the manager owns and disposes the
/// default instance too. Factories should return instances the manager may dispose (prefer
/// <c>ActivatorUtilities.CreateInstance</c> over resolving an unrelated container-owned singleton).</para>
/// <para><b>Thread safety:</b> all members are thread-safe.</para>
/// </remarks>
public abstract class Manager<TDriver, TOptions> : IAsyncDisposable, IDisposable
    where TDriver : notnull
    where TOptions : class, IManagerOptions
{
    private sealed class Entry
    {
        public TaskCompletionSource<TDriver> Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TDriver> Task => Source.Task;

        /// <summary>Set (under the lock) when the entry left the cache while creation was still running.</summary>
        public bool Retired { get; set; }

        /// <summary>
        /// Set when a synchronous creation found that the factory had been replaced by an asynchronous one. The refusal is for the
        /// synchronous caller; an asynchronous caller that shared the entry starts over with the new factory.
        /// </summary>
        public bool SyncRefused { get; set; }
    }

    private readonly IServiceProvider _provider;
    private readonly ContainerOwnedDrivers<TDriver>? _containerOwnedDrivers;
    private readonly IDriverRegistry<TDriver> _registry;
    private readonly IOptionsMonitor<TOptions> _options;
    private readonly IDisposable? _optionsSubscription;
    private readonly Action<string> _onRegistered;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TDriver> _retired = new();
    private string _storesFingerprint;
    private volatile bool _disposed;

    /// <summary>Creates the manager.</summary>
    /// <param name="provider">Handed to driver factories.</param>
    /// <param name="registry">The registry holding driver factories for <typeparamref name="TDriver"/>.</param>
    /// <param name="options">Live view of the module options (<c>Default</c> and <c>Stores</c>).</param>
    protected Manager(IServiceProvider provider, IDriverRegistry<TDriver> registry, IOptionsMonitor<TOptions> options)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);

        _provider = provider;
        _containerOwnedDrivers = (ContainerOwnedDrivers<TDriver>?)provider.GetService(typeof(ContainerOwnedDrivers<TDriver>));
        _registry = registry;
        _options = options;
        _storesFingerprint = Fingerprint(options.CurrentValue);

        _onRegistered = name => Forget(name);
        _registry.Registered += _onRegistered;
        _optionsSubscription = options.OnChange((current, optionsName) =>
        {
            if (string.IsNullOrEmpty(optionsName))
            {
                OnOptionsChanged(current);
            }
        });
    }

    /// <summary>
    /// The default driver name, read from the options on every access so a changed <c>Default</c> takes effect
    /// immediately. Override to add module-specific fallbacks.
    /// </summary>
    public virtual string DefaultDriverName => _options.CurrentValue.Default;

    /// <summary>Gets a driver synchronously (the default driver when <paramref name="name"/> is <see langword="null"/>).</summary>
    /// <param name="name">Driver name, or <see langword="null"/> for the default.</param>
    /// <returns>The cached driver, created on first use.</returns>
    /// <exception cref="DriverNotRegisteredException">The name is not registered.</exception>
    /// <exception cref="InvalidOperationException">
    /// No default is configured, or the driver only has an asynchronous factory and is not created yet
    /// (use <see cref="DriverAsync"/>).
    /// </exception>
    /// <exception cref="ObjectDisposedException">The manager was disposed.</exception>
    public TDriver Driver(string? name = null)
    {
        var resolved = ResolveName(name);

        Entry? cached;
        lock (_gate)
        {
            ThrowIfDisposed();
            _cache.TryGetValue(resolved, out cached);
        }

        if (cached is { Task.IsCompletedSuccessfully: true })
        {
            return cached.Task.Result;
        }

        if (_registry.IsAsyncOnly(resolved))
        {
            throw AsyncOnlyNotCreated(resolved, inner: null);
        }

        var entry = GetOrStart(resolved, synchronous: true);
        try
        {
            // Sync factories complete inline; a concurrent sync creator is awaited briefly. The creation above goes through
            // IDriverRegistry.Create, which reads the registered factory once and refuses an asynchronous one, so a factory that was
            // replaced by an asynchronous one after the check above cannot make this wait on asynchronous work.
            return entry.Task.GetAwaiter().GetResult();
        }
        catch when (entry.Task.IsFaulted || entry.Task.IsCanceled)
        {
            Evict(resolved, entry);
            throw;
        }
    }

    /// <summary>Gets a driver asynchronously; supports both synchronous and asynchronous factories.</summary>
    /// <param name="name">Driver name, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">
    /// Cancels this caller's wait only; a creation shared with other callers keeps running and its result is cached.
    /// </param>
    /// <returns>The cached driver, created on first use.</returns>
    /// <exception cref="DriverNotRegisteredException">The name is not registered.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The manager was disposed.</exception>
    public async ValueTask<TDriver> DriverAsync(string? name = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = ResolveName(name);
        while (true)
        {
            var entry = GetOrStart(resolved, synchronous: false);
            try
            {
                return await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch when (entry.Task.IsFaulted || entry.Task.IsCanceled)
            {
                Evict(resolved, entry);
                if (!entry.SyncRefused)
                {
                    throw;
                }

                // A synchronous caller created this entry and found that the factory had just been replaced by an asynchronous one.
                // That refusal is for it, not for this caller, so start over: the next entry uses the new factory.
            }
        }
    }

    /// <summary>Registers or replaces a synchronous driver factory at runtime (Laravel's <c>extend()</c>).</summary>
    /// <param name="name">Driver name.</param>
    /// <param name="factory">Builds the driver.</param>
    /// <remarks>A cached instance under the same name is evicted so the next call uses the new factory.</remarks>
    public void Extend(string name, Func<IServiceProvider, TDriver> factory) => _registry.Register(name, factory);

    /// <summary>Registers or replaces an asynchronous driver factory at runtime.</summary>
    /// <param name="name">Driver name.</param>
    /// <param name="factory">Builds the driver asynchronously.</param>
    public void Extend(string name, Func<IServiceProvider, CancellationToken, ValueTask<TDriver>> factory) =>
        _registry.Register(name, factory);

    /// <summary>
    /// Drops the cached driver so the next request builds a fresh one (Laravel's <c>forgetDriver()</c>).
    /// The old instance is disposed with the manager, not immediately, because it may still be in use.
    /// </summary>
    /// <param name="name">Driver name.</param>
    /// <returns><see langword="true"/> if a cached driver existed.</returns>
    public bool Forget(string name)
    {
        lock (_gate)
        {
            if (_disposed || !_cache.Remove(name, out var entry))
            {
                return false;
            }

            RetireLocked(entry);
            return true;
        }
    }

    /// <summary>Drops every cached driver (Laravel's <c>forgetDrivers()</c>). See <see cref="Forget"/>.</summary>
    public void ForgetAll()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            RetireAllLocked();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var owned = BeginDispose(out var errors);
        if (owned is null)
        {
            return;
        }

        foreach (var driver in owned)
        {
            try
            {
                switch (driver)
                {
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                    case IAsyncDisposable asyncDisposable:
                        asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                        break;
                }
            }
            catch (Exception ex)
            {
                (errors ??= new()).Add(ex);
            }
        }

        FinishDispose(errors);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        var owned = BeginDispose(out var errors);
        if (owned is null)
        {
            return;
        }

        foreach (var driver in owned)
        {
            try
            {
                switch (driver)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                (errors ??= new()).Add(ex);
            }
        }

        FinishDispose(errors);
    }

    private string ResolveName(string? name)
    {
        ThrowIfDisposed();
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Driver name must not be blank.", nameof(name));
        }

        var resolved = name ?? DefaultDriverName;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            throw new InvalidOperationException(
                $"No default driver is configured for {typeof(TDriver).Name}. " +
                "Set the 'Default' option or pass a driver name explicitly.");
        }

        if (!_registry.IsRegistered(resolved))
        {
            throw new DriverNotRegisteredException(resolved, typeof(TDriver).Name, _registry.Names);
        }

        return resolved;
    }

    private Entry GetOrStart(string name, bool synchronous)
    {
        Entry entry;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_cache.TryGetValue(name, out var existing) && !(existing.Task.IsFaulted || existing.Task.IsCanceled))
            {
                return existing;
            }

            entry = new Entry();
            _cache[name] = entry;
        }

        // Runs synchronously until the first real await, so sync factories finish before this returns.
        _ = RunCreationAsync(name, entry, synchronous);
        return entry;
    }

    private static InvalidOperationException AsyncOnlyNotCreated(string name, Exception? inner) =>
        new(
            $"Driver '{name}' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.",
            inner);

    // The registry reads the registered factory once and throws if it is asynchronous. That can only happen here when the factory was
    // replaced after the caller's IsAsyncOnly check: the caller gets the same exception as for an asynchronous-only driver, and
    // asynchronous callers that shared the entry are told to start over (see DriverAsync).
    private TDriver CreateSynchronously(string name, Entry entry)
    {
        try
        {
            return _registry.Create(name, _provider);
        }
        catch (InvalidOperationException ex) when (_registry.IsAsyncOnly(name))
        {
            entry.SyncRefused = true;
            throw AsyncOnlyNotCreated(name, ex);
        }
    }

    private async Task RunCreationAsync(string name, Entry entry, bool synchronous)
    {
        TDriver driver;
        try
        {
            driver = synchronous
                ? CreateSynchronously(name, entry)
                : await _registry.CreateAsync(name, _provider, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            entry.Source.TrySetCanceled(ex.CancellationToken);
            return;
        }
        catch (Exception ex)
        {
            entry.Source.TrySetException(ex);
            return;
        }

        bool disposedMeanwhile;
        lock (_gate)
        {
            disposedMeanwhile = _disposed;
            if (!disposedMeanwhile)
            {
                if (entry.Retired)
                {
                    _retired.Add(driver);
                }

                entry.Source.TrySetResult(driver);
            }
        }

        if (disposedMeanwhile)
        {
            await DisposeQuietlyAsync(driver).ConfigureAwait(false);
            entry.Source.TrySetException(new ObjectDisposedException(GetType().Name));
        }
    }

    private void Evict(string name, Entry entry)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(name, out var current) && ReferenceEquals(current, entry))
            {
                _cache.Remove(name);
            }
        }
    }

    private void OnOptionsChanged(TOptions current)
    {
        // Any configuration reload fires OnChange, even for unrelated keys, so compare a fingerprint of the stores
        // and only rebuild drivers when their configuration really changed (PDR-004). Default is read per call.
        var fingerprint = Fingerprint(current);
        lock (_gate)
        {
            if (_disposed || string.Equals(fingerprint, _storesFingerprint, StringComparison.Ordinal))
            {
                return;
            }

            _storesFingerprint = fingerprint;
            RetireAllLocked();
        }
    }

    private void RetireAllLocked()
    {
        foreach (var entry in _cache.Values)
        {
            RetireLocked(entry);
        }

        _cache.Clear();
    }

    private void RetireLocked(Entry entry)
    {
        if (entry.Task.IsCompletedSuccessfully)
        {
            _retired.Add(entry.Task.Result);
        }
        else if (!entry.Task.IsCompleted)
        {
            entry.Retired = true; // RunCreationAsync moves the result to _retired when it finishes
        }
    }

    private List<object>? BeginDispose(out List<Exception>? errors)
    {
        errors = null;
        List<object> owned = new();
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            _disposed = true;
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            foreach (var entry in _cache.Values)
            {
                if (entry.Task.IsCompletedSuccessfully)
                {
                    var driver = entry.Task.Result;
                    if (seen.Add(driver) && _containerOwnedDrivers?.Contains(driver) != true)
                    {
                        owned.Add(driver);
                    }
                }
            }

            foreach (var driver in _retired)
            {
                if (seen.Add(driver) && _containerOwnedDrivers?.Contains(driver) != true)
                {
                    owned.Add(driver);
                }
            }

            _cache.Clear();
            _retired.Clear();
        }

        // The drivers are out of the cache by now, so nothing here may keep the caller from disposing them. Each step that can throw is
        // guarded and its failure is kept to be reported with the failures of the drivers. Cancelling the lifetime token runs the
        // callbacks that were registered on it (a factory can register one) and rethrows what they threw, after all of them ran.
        try
        {
            _registry.Registered -= _onRegistered;
        }
        catch (Exception ex)
        {
            (errors ??= new()).Add(ex);
        }

        try
        {
            _optionsSubscription?.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= new()).Add(ex);
        }

        try
        {
            _lifetime.Cancel();
        }
        catch (AggregateException ex)
        {
            (errors ??= new()).AddRange(ex.InnerExceptions);
        }

        return owned;
    }

    private void FinishDispose(List<Exception>? errors)
    {
        _lifetime.Dispose();
        if (errors is not null)
        {
            throw new AggregateException(
                "The manager did not dispose cleanly: one or more drivers, lifetime callbacks or subscriptions failed.",
                errors);
        }
    }

    private static async ValueTask DisposeQuietlyAsync(TDriver driver)
    {
        try
        {
            switch (driver)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
        catch
        {
            // Nobody is left to observe the failure; the manager is already disposed.
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    private static string Fingerprint(IManagerOptions options)
    {
        var builder = new StringBuilder();
        foreach (var store in options.Stores.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            Append(builder, 'S', store.Key.ToLowerInvariant());
            if (store.Value is null)
            {
                continue;
            }

            // AsEnumerable(makePathsRelative: true) lists the keys below the store but not the value of the store itself.
            Append(builder, 'R', store.Value.Value);
            foreach (var pair in store.Value.AsEnumerable(makePathsRelative: true)
                         .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                Append(builder, 'K', pair.Key);
                Append(builder, 'V', pair.Value);
            }
        }

        return builder.ToString();
    }

    // Every field is written as a tag, its length and its text (a tag and '-' for null), so a key or a value that contains '=', ';'
    // or '[' cannot imitate a separator and a missing value differs from an empty one: two different lists of keys and values never
    // produce the same string, which is what the comparison in OnOptionsChanged relies on.
    private static void Append(StringBuilder builder, char tag, string? text)
    {
        builder.Append(tag);
        if (text is null)
        {
            builder.Append('-');
        }
        else
        {
            builder.Append(text.Length).Append(':').Append(text);
        }
    }
}
