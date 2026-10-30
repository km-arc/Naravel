using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;
using Naravel.Cache.Stores;
using Naravel.Broadcasting;
using Naravel.Broadcasting.Drivers;
using Naravel.Events;
using Naravel.Foundation;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the broadcasting module and the event bridge.</summary>
public static class BroadcastingServiceCollectionExtensions
{
    /// <summary>
    /// Registers SignalR, <see cref="BroadcastManager"/>, an <see cref="IBroadcaster"/> for the default driver and the built-in <c>signalr</c>, <c>log</c> and <c>null</c> drivers.
    /// Configure <c>Broadcasting:Default</c> and <c>Broadcasting:Stores:&lt;name&gt;:Driver</c>; add channel rules in <paramref name="configure"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> <c>BroadcastServiceProvider</c> and <c>routes/channels.php</c>.</para>
    /// <para><b>Why it exists:</b> SignalR delivers messages but has no typed event contract, no channel authorization by default and no fake.</para>
    /// <para><b>Not ported on purpose:</b> vendor drivers (Pusher, Ably, Reverb) and the <c>/broadcasting/auth</c> endpoint: SignalR authorizes when a client joins. A Redis backplane stays a native SignalR option the application sets up.</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configure">Optional: registers channel rules, for example <c>o.Channels.Authorize("private-orders.{id}", (user, id) =&gt; ...)</c>.</param>
    /// <param name="sectionName">The configuration section, <c>Broadcasting</c> by default.</param>
    public static IServiceCollection AddNaravelBroadcasting(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BroadcastingOptions>? configure = null,
        string sectionName = "Broadcasting")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddSignalR();

        services.AddNaravelDriver<IBroadcastDriver>("signalr", CreateSignalR);
        services.AddNaravelDriver<IBroadcastDriver>("log", CreateLog);
        services.AddNaravelDriver<IBroadcastDriver>("null", _ => new NullBroadcastDriver());
        AddConfiguredStores(services, configuration, sectionName, "signalr", CreateSignalR);
        AddConfiguredStores(services, configuration, sectionName, "log", CreateLog);
        AddConfiguredStores(services, configuration, sectionName, "null", _ => new NullBroadcastDriver());

        BindOptions(services, configuration.GetSection(sectionName));
        services.AddNaravelManager<BroadcastManager, IBroadcastDriver, BroadcastingOptions>();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton<IBroadcaster>(provider => provider.GetRequiredService<BroadcastManager>().Broadcaster());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PresenceTracker>();
        services.TryAddSingleton(provider => CreatePresenceStore(provider));
        services.TryAddSingleton<PresenceService>();
        services.TryAddSingleton<IPresence>(provider => provider.GetRequiredService<PresenceService>());
        services.AddHostedService<PresenceHeartbeatService>();
        return services;
    }

    /// <summary>
    /// Binds the three configurable values (<c>Default</c>, <c>Stores</c>, <c>PresenceEntryTtl</c>) by hand and
    /// follows configuration reloads like <c>Configure&lt;TOptions&gt;(section)</c> does.
    /// </summary>
    /// <remarks>
    /// <see cref="BroadcastingOptions.Channels"/> is code-only (rules are delegates) and its type cannot be created
    /// from configuration, so the configuration binding generator rejects the whole options type; the reflection binder
    /// would not be trim or AOT safe.
    /// </remarks>
    private static void BindOptions(IServiceCollection services, IConfiguration section)
    {
        services.Configure<BroadcastingOptions>(options =>
        {
            if (section["Default"] is { } defaultStore) options.Default = defaultStore;
            if (section["PresenceEntryTtl"] is { Length: > 0 } ttl)
            {
                options.PresenceEntryTtl = TimeSpan.Parse(ttl, System.Globalization.CultureInfo.InvariantCulture);
            }

            foreach (var store in section.GetSection("Stores").GetChildren())
            {
                options.Stores[store.Key] = store;
            }
        });
        services.AddSingleton<IOptionsChangeTokenSource<BroadcastingOptions>>(
            new ConfigurationChangeTokenSource<BroadcastingOptions>(Microsoft.Extensions.Options.Options.DefaultName, section));
    }

    /// <summary>
    /// Broadcasts <typeparamref name="TEvent"/> whenever <c>Naravel.Events</c> dispatches exactly that type.
    /// </summary>
    /// <remarks>
    /// <b>Why it exists:</b> <c>Naravel.Events</c> matches listeners by exact event type, with no base-type or interface dispatch, so "broadcast everything that is <see cref="IBroadcastable"/>" cannot be a single listener; each event is opted in explicitly.
    /// </remarks>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddBroadcastOn<TEvent>(this IServiceCollection services)
        where TEvent : class, IBroadcastable
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventListener<TEvent>, BroadcastListener<TEvent>>());
        return services;
    }

    // Presence lists live in the application's cache (Redis or Memcached when there are several instances). Without a registered cache a process-local one is used, and the store warns once when its lock is not shared.
    private static PresenceStore CreatePresenceStore(IServiceProvider provider)
    {
        var cache = provider.GetService<ICacheStore>();
        var locks = provider.GetService<ICacheLock>();
        if (cache is null || locks is null)
        {
            cache = new MemoryCacheStore("broadcasting-presence", new MemoryCache(new MemoryCacheOptions()));
            locks = new MemoryLock("broadcasting-presence");
        }

        return new PresenceStore(
            cache,
            locks,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptionsMonitor<BroadcastingOptions>>(),
            provider.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Broadcasting") ?? NullLogger.Instance);
    }

    private static IBroadcastDriver CreateSignalR(IServiceProvider provider) =>
        new SignalRBroadcastDriver(provider.GetRequiredService<IHubContext<BroadcastHub>>());

    private static IBroadcastDriver CreateLog(IServiceProvider provider) =>
        new LogBroadcastDriver(provider.GetService<ILoggerFactory>()?.CreateLogger("Naravel.Broadcasting") ?? NullLogger.Instance);

    private static void AddConfiguredStores(
        IServiceCollection services,
        IConfiguration configuration,
        string sectionName,
        string driverName,
        Func<IServiceProvider, IBroadcastDriver> factory)
    {
        foreach (var store in configuration.GetSection($"{sectionName}:Stores").GetChildren())
        {
            if (!string.Equals(store["Driver"], driverName, StringComparison.OrdinalIgnoreCase)) continue;
            services.AddNaravelDriver<IBroadcastDriver>(store.Key, factory);
        }
    }
}
