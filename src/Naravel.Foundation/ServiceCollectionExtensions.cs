using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Naravel.Foundation;

/// <summary>Carries a deferred "register built-in drivers" action collected before the registry is created.</summary>
internal sealed class DriverRegistrationCallback<TDriver>(Action<IDriverRegistry<TDriver>> configure)
    where TDriver : notnull
{
    public Action<IDriverRegistry<TDriver>> Configure { get; } = configure;
}

/// <summary>DI wiring for Naravel managers and their drivers.</summary>
/// <remarks>
/// <b>Laravel equivalent:</b> a service provider's <c>$this->app->singleton(CacheManager::class)</c> plus
/// <c>bind('cache.store', ...)</c>. Implements the "hybrid" consumption model of PDR-003: the manager is available
/// for runtime selection, and the default driver is also registered as a plain driver service.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a manager, its driver registry, its options, and the default driver as a plain service.
    /// </summary>
    /// <typeparam name="TManager">The concrete manager (for example <c>CacheManager</c>).</typeparam>
    /// <typeparam name="TDriver">The driver contract.</typeparam>
    /// <typeparam name="TOptions">The module options bound from <paramref name="configuration"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The module's section, for example <c>configuration.GetSection("Cache")</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The plain <typeparamref name="TDriver"/> registration resolves the default driver <b>once</b>, on first
    /// injection. Consumers that must follow a changing <c>Default</c> or pick drivers at runtime should inject the
    /// manager instead. Register built-in drivers with <see cref="AddNaravelDriver{TDriver}(IServiceCollection, string, Func{IServiceProvider, TDriver})"/>
    /// before the service provider is built.
    /// </remarks>
    [RequiresUnreferencedCode("Binds TOptions from configuration through reflection. Trimmed or Native AOT applications bind the options with a compile-time Configure<TOptions>(section) call (configuration binding generator) and register the manager with AddNaravelManager<TManager, TDriver, TOptions>(services).")]
    [RequiresDynamicCode("Binds TOptions from configuration through reflection. Trimmed or Native AOT applications bind the options with a compile-time Configure<TOptions>(section) call (configuration binding generator) and register the manager with AddNaravelManager<TManager, TDriver, TOptions>(services).")]
    public static IServiceCollection AddNaravelManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager, TDriver, TOptions>(
        this IServiceCollection services,
        IConfiguration configuration)
        where TManager : Manager<TDriver, TOptions>
        where TDriver : class
        where TOptions : class, IManagerOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TOptions>(configuration);
        return services.AddNaravelManager<TManager, TDriver, TOptions>();
    }

    /// <summary>
    /// Registers a manager, its driver registry and the default driver as a plain service, without binding
    /// <typeparamref name="TOptions"/>: the caller registers the options itself.
    /// </summary>
    /// <typeparam name="TManager">The concrete manager (for example <c>CacheManager</c>).</typeparam>
    /// <typeparam name="TDriver">The driver contract.</typeparam>
    /// <typeparam name="TOptions">The module options; they must be registered separately, for example with <c>services.Configure&lt;TOptions&gt;(section)</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// This is the trim and Native AOT path: <c>Configure&lt;TOptions&gt;(section)</c> with a concrete options type is
    /// bound at compile time when the project enables the configuration binding generator
    /// (<c>EnableConfigurationBindingGenerator</c>), whereas the generic call inside
    /// <see cref="AddNaravelManager{TManager, TDriver, TOptions}(IServiceCollection, IConfiguration)"/> cannot be.
    /// Everything else is identical to that overload.
    /// </remarks>
    public static IServiceCollection AddNaravelManager<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TManager, TDriver, TOptions>(
        this IServiceCollection services)
        where TManager : Manager<TDriver, TOptions>
        where TDriver : class
        where TOptions : class, IManagerOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IDriverRegistry<TDriver>>(sp =>
        {
            var registry = new DriverRegistry<TDriver>();
            foreach (var callback in sp.GetServices<DriverRegistrationCallback<TDriver>>())
            {
                callback.Configure(registry);
            }

            return registry;
        });
        services.TryAddSingleton<TManager>();
        services.TryAddSingleton<TDriver>(sp => sp.GetRequiredService<TManager>().Driver());
        return services;
    }

    /// <summary>Registers several built-in drivers at once; the action runs when the registry is created.</summary>
    /// <typeparam name="TDriver">The driver contract.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Receives the registry to register factories on.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection ConfigureNaravelDrivers<TDriver>(
        this IServiceCollection services,
        Action<IDriverRegistry<TDriver>> configure)
        where TDriver : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddSingleton(new DriverRegistrationCallback<TDriver>(configure));
        return services;
    }

    /// <summary>Registers one built-in synchronous driver factory.</summary>
    /// <typeparam name="TDriver">The driver contract.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Driver name.</param>
    /// <param name="factory">Builds the driver.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNaravelDriver<TDriver>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, TDriver> factory)
        where TDriver : class =>
        services.ConfigureNaravelDrivers<TDriver>(registry => registry.Register(name, factory));

    /// <summary>Registers one built-in asynchronous driver factory.</summary>
    /// <typeparam name="TDriver">The driver contract.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Driver name.</param>
    /// <param name="factory">Builds the driver asynchronously.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddNaravelDriver<TDriver>(
        this IServiceCollection services,
        string name,
        Func<IServiceProvider, CancellationToken, ValueTask<TDriver>> factory)
        where TDriver : class =>
        services.ConfigureNaravelDrivers<TDriver>(registry => registry.Register(name, factory));
}
