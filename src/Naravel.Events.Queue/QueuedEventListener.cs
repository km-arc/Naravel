using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Naravel.Events;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Extensions;
using Naravel.Queue.Jobs;
using Naravel.Events.Queue;

namespace Microsoft.Extensions.DependencyInjection
{

/// <summary>
/// Registration methods for the optional Naravel.Events.Queue adapter.
/// </summary>
public static class NaravelEventsQueueServiceCollectionExtensions
{
    /// <summary>
    /// Adds the queue-aware listener invoker and registers the adapter's internal queue job.
    /// Call <c>AddQueue</c> and a Queue driver registration for dispatch and worker execution.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> queued event listener registration.</para>
    /// <para><b>Why it exists:</b> it keeps the Events core independent of Queue and makes queued execution opt-in.</para>
    /// <para><b>Not ported on purpose:</b> automatic discovery, implicit queue selection, and dynamic type loading.</para>
    /// </remarks>
    public static IServiceCollection AddNaravelEventsQueue(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNaravelEvents();
        services.Replace(ServiceDescriptor.Scoped<IEventListenerInvoker, QueueEventListenerInvoker>());
        services.TryAddSingleton<QueuedEventListenerRegistry>();
        services.AddJob(Naravel.Events.Queue.EventsQueueJsonContext.Default.QueuedEventListenerJob, "naravel.events.queued-listener");
        return services;
    }

    /// <summary>
    /// Registers a typed listener for queued execution under a stable, explicit alias.
    /// </summary>
    /// <typeparam name="TEvent">The event type handled by the listener.</typeparam>
    /// <typeparam name="TListener">A DI-resolved queued listener type.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="alias">Stable identifier stored in queued messages.</param>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> an explicitly registered queued listener.</para>
    /// <para><b>Why it exists:</b> the worker can resolve listener behavior from an allow-listed alias without
    /// resolving a CLR type from queued data.</para>
    /// <para><b>Not ported on purpose:</b> assembly scanning and type names in serialized payloads.</para>
    /// </remarks>
    [RequiresUnreferencedCode("Serialises the event with reflection-based System.Text.Json. When trimming or publishing with Native AOT use the overload that takes a JsonTypeInfo<TEvent> from a source-generated JsonSerializerContext.")]
    [RequiresDynamicCode("Serialises the event with reflection-based System.Text.Json. When publishing with Native AOT use the overload that takes a JsonTypeInfo<TEvent> from a source-generated JsonSerializerContext.")]
    public static IServiceCollection AddQueuedEventListener<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TListener>(this IServiceCollection services, string alias)
        where TListener : class, IQueuedEventListener<TEvent>
        => AddListener<TEvent, TListener>(services, alias, null, null);

    /// <summary>
    /// Registers a typed listener for queued execution under an explicit alias, with its own dispatch options
    /// (connection, queue, delay, attempts, priority).
    /// </summary>
    /// <typeparam name="TEvent">The event type handled by the listener.</typeparam>
    /// <typeparam name="TListener">A DI-resolved queued listener type.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="alias">Stable identifier stored in queued messages.</param>
    /// <param name="configureDispatch">Applied to a fresh <see cref="DispatchOptions"/> for every dispatch of this listener.</param>
    /// <remarks><para><b>Laravel equivalent:</b> the <c>$connection</c>, <c>$queue</c> and <c>$delay</c> properties of a queued listener.</para></remarks>
    [RequiresUnreferencedCode("Serialises the event with reflection-based System.Text.Json. When trimming or publishing with Native AOT use the overload that takes a JsonTypeInfo<TEvent> from a source-generated JsonSerializerContext.")]
    [RequiresDynamicCode("Serialises the event with reflection-based System.Text.Json. When publishing with Native AOT use the overload that takes a JsonTypeInfo<TEvent> from a source-generated JsonSerializerContext.")]
    public static IServiceCollection AddQueuedEventListener<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TListener>(this IServiceCollection services, string alias, Action<DispatchOptions> configureDispatch)
        where TListener : class, IQueuedEventListener<TEvent>
    {
        ArgumentNullException.ThrowIfNull(configureDispatch);
        return AddListener<TEvent, TListener>(services, alias, null, configureDispatch);
    }

    /// <summary>
    /// Registers a typed listener whose event payload is (de)serialized with source-generated metadata, so the
    /// adapter needs no reflection for it and works with trimming and Native AOT.
    /// </summary>
    /// <typeparam name="TEvent">The event type handled by the listener.</typeparam>
    /// <typeparam name="TListener">A DI-resolved queued listener type.</typeparam>
    /// <param name="services">The application service collection.</param>
    /// <param name="alias">Stable identifier stored in queued messages.</param>
    /// <param name="typeInfo">Source-generated metadata for <typeparamref name="TEvent"/>.</param>
    /// <param name="configureDispatch">Optional per-dispatch options, as in the other overload.</param>
    public static IServiceCollection AddQueuedEventListener<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TListener>(this IServiceCollection services, string alias, JsonTypeInfo<TEvent> typeInfo, Action<DispatchOptions>? configureDispatch = null)
        where TListener : class, IQueuedEventListener<TEvent>
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return AddListener<TEvent, TListener>(services, alias, typeInfo, configureDispatch);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "The reflection-based branches run only when typeInfo is null, which only the public overloads marked RequiresUnreferencedCode pass; the JsonTypeInfo overload never reaches them.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "See IL2026.")]
    private static IServiceCollection AddListener<TEvent, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TListener>(IServiceCollection services, string alias, JsonTypeInfo<TEvent>? typeInfo, Action<DispatchOptions>? configureDispatch)
        where TListener : class, IQueuedEventListener<TEvent>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        services.TryAddScoped<TListener>();
        services.AddScoped<IEventListener<TEvent>>(sp => sp.GetRequiredService<TListener>());
        services.Configure<QueuedEventListenerOptions>(options =>
            options.Registrations.Add(new QueuedEventListenerRegistration(
                alias,
                typeof(TEvent),
                typeof(TListener),
                async (provider, payload, cancellationToken) =>
                {
                    var evt = typeInfo is null ? JsonSerializer.Deserialize<TEvent>(payload) : JsonSerializer.Deserialize(payload, typeInfo);
                    if (evt is null)
                    {
                        throw new InvalidDataException($"Queued event payload for alias '{alias}' was null.");
                    }

                    var listener = provider.GetRequiredService<TListener>();
                    await listener.HandleAsync(evt, new EventDispatchContext(), cancellationToken).ConfigureAwait(false);
                },
                evt => typeInfo is null ? JsonSerializer.Serialize((TEvent)evt!) : JsonSerializer.Serialize((TEvent)evt!, typeInfo),
                configureDispatch)));

        return services;
    }
}
}

namespace Naravel.Events.Queue
{
/// <summary>
/// Queue job that invokes one explicitly registered event listener.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the queued wrapper for a Laravel event listener.</para>
/// <para><b>Why it exists:</b> it carries only an explicit listener alias and serialized event payload.</para>
/// <para><b>Not ported on purpose:</b> serialized CLR type names or implicit listener discovery.</para>
/// </remarks>
public sealed class QueuedEventListenerJob : Job
{
    /// <summary>Stable allow-listed listener identifier.</summary>
    public string ListenerAlias { get; set; } = string.Empty;

    /// <summary>Serialized event data, interpreted by the listener's registered generic event type.</summary>
    public string EventPayload { get; set; } = string.Empty;

    /// <summary>
    /// Resolves the explicitly registered listener executor from the job scope and invokes the listener.
    /// </summary>
    public override Task HandleAsync(JobContext context, CancellationToken cancellationToken)
        => context.Services.GetRequiredService<QueuedEventListenerRegistry>()
            .ExecuteAsync(ListenerAlias, EventPayload, context.Services, cancellationToken);
}

internal sealed class QueueEventListenerInvoker(
    IJobDispatcher dispatcher,
    QueuedEventListenerRegistry registry) : IEventListenerInvoker
{
    public Task InvokeAsync<TEvent>(
        IEventListener<TEvent> listener,
        TEvent evt,
        EventDispatchContext context,
        CancellationToken cancellationToken)
    {
        if (listener is not IQueuedEventListener<TEvent>)
        {
            return listener.HandleAsync(evt, context, cancellationToken);
        }

        var registration = registry.Get(typeof(TEvent), listener.GetType());
        var job = new QueuedEventListenerJob
        {
            ListenerAlias = registration.Alias,
            EventPayload = registration.Serialize(evt!)
        };

        return dispatcher.DispatchAsync(job, registration.ConfigureDispatch, cancellationToken);
    }
}

internal sealed class QueuedEventListenerOptions
{
    public List<QueuedEventListenerRegistration> Registrations { get; } = new();
}

internal sealed record QueuedEventListenerRegistration(
    string Alias,
    Type EventType,
    Type ListenerType,
    Func<IServiceProvider, string, CancellationToken, Task> Execute,
    Func<object, string> Serialize,
    Action<DispatchOptions>? ConfigureDispatch);

internal sealed class QueuedEventListenerRegistry
{
    private readonly IReadOnlyDictionary<string, QueuedEventListenerRegistration> _byAlias;
    private readonly IReadOnlyDictionary<(Type EventType, Type ListenerType), QueuedEventListenerRegistration> _byListener;

    public QueuedEventListenerRegistry(IOptions<QueuedEventListenerOptions> options)
    {
        var aliases = new Dictionary<string, QueuedEventListenerRegistration>(StringComparer.Ordinal);
        var listeners = new Dictionary<(Type EventType, Type ListenerType), QueuedEventListenerRegistration>();
        foreach (var registration in options.Value.Registrations)
        {
            if (!aliases.TryAdd(registration.Alias, registration))
            {
                throw new InvalidOperationException($"Queued event listener alias '{registration.Alias}' is registered more than once.");
            }

            if (!listeners.TryAdd((registration.EventType, registration.ListenerType), registration))
            {
                throw new InvalidOperationException(
                    $"Queued event listener '{registration.ListenerType}' for '{registration.EventType}' is registered more than once.");
            }
        }

        _byAlias = aliases;
        _byListener = listeners;
    }

    public QueuedEventListenerRegistration Get(Type eventType, Type listenerType)
        => _byListener.TryGetValue((eventType, listenerType), out var registration)
            ? registration
            : throw new InvalidOperationException(
                $"Queued listener '{listenerType}' for event '{eventType}' has no explicit queue alias registration.");

    public Task ExecuteAsync(string alias, string payload, IServiceProvider provider, CancellationToken cancellationToken)
        => _byAlias.TryGetValue(alias, out var registration)
            ? registration.Execute(provider, payload, cancellationToken)
            : throw new InvalidOperationException($"Queued event listener alias '{alias}' is not registered.");
}
}
