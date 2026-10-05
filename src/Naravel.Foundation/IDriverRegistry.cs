namespace Naravel.Foundation;

/// <summary>
/// The source of truth for "which named drivers exist and how to build them" for one driver contract.
/// </summary>
/// <typeparam name="TDriver">The driver contract (for example an <c>ICacheDriver</c> interface).</typeparam>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>$customCreators</c> array plus the <c>createXxxDriver()</c> methods
/// inside <c>Illuminate\Support\Manager</c>, and its <c>extend()</c> method.</para>
/// <para><b>Why it exists instead of Keyed DI:</b> .NET keyed services can only be registered before the
/// service provider is built. Naravel requires drivers to be addable at runtime (Laravel's
/// <c>Cache::extend()</c>), so the registry is a thread-safe, always-open dictionary of factories.</para>
/// <para><b>Lifetime:</b> register as a singleton, one instance per <typeparamref name="TDriver"/>.
/// Names are case-insensitive because .NET configuration keys are.</para>
/// </remarks>
public interface IDriverRegistry<TDriver> where TDriver : notnull
{
    /// <summary>
    /// Raised after a factory is registered or replaced, with the driver name. <see cref="Manager{TDriver, TOptions}"/>
    /// uses it to evict a stale cached instance.
    /// </summary>
    event Action<string>? Registered;

    /// <summary>Names currently registered, sorted alphabetically (a snapshot).</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>Registers (or replaces) a synchronous factory under <paramref name="name"/>.</summary>
    /// <param name="name">Driver name; must not be blank.</param>
    /// <param name="factory">Builds the driver. Receives the application's service provider.</param>
    void Register(string name, Func<IServiceProvider, TDriver> factory);

    /// <summary>Registers (or replaces) an asynchronous factory under <paramref name="name"/>.</summary>
    /// <param name="name">Driver name; must not be blank.</param>
    /// <param name="factory">Builds the driver asynchronously; observe the supplied token.</param>
    void Register(string name, Func<IServiceProvider, CancellationToken, ValueTask<TDriver>> factory);

    /// <summary>Returns <see langword="true"/> if a factory exists for <paramref name="name"/>.</summary>
    /// <param name="name">Driver name.</param>
    bool IsRegistered(string name);

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="name"/> is registered with an asynchronous factory only
    /// (and therefore cannot be created through <see cref="Create"/>).
    /// </summary>
    /// <param name="name">Driver name.</param>
    bool IsAsyncOnly(string name);

    /// <summary>Builds a driver synchronously.</summary>
    /// <param name="name">Driver name.</param>
    /// <param name="provider">Service provider handed to the factory.</param>
    /// <exception cref="DriverNotRegisteredException">No factory is registered for the name.</exception>
    /// <exception cref="InvalidOperationException">The name was registered with an asynchronous factory.</exception>
    TDriver Create(string name, IServiceProvider provider);

    /// <summary>Builds a driver; works for both synchronous and asynchronous factories.</summary>
    /// <param name="name">Driver name.</param>
    /// <param name="provider">Service provider handed to the factory.</param>
    /// <param name="cancellationToken">Passed to asynchronous factories.</param>
    /// <exception cref="DriverNotRegisteredException">No factory is registered for the name.</exception>
    ValueTask<TDriver> CreateAsync(string name, IServiceProvider provider, CancellationToken cancellationToken = default);
}
