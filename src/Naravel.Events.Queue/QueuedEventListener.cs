using System.Text.Json;
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
        services.AddJob<QueuedEventListenerJob>("naravel.events.queued-listener");
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
    public static IServiceCollection AddQueuedEventListener<TEvent, TListener>(this IServiceCollection services, string alias)
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
                    var evt = JsonSerializer.Deserialize<TEvent>(payload);
                    if (evt is null)
                    {
                        throw new InvalidDataException($"Queued event payload for alias '{alias}' was null.");
                    }

                    var listener = provider.GetRequiredService<TListener>();
                    await listener.HandleAsync(evt, new EventDispatchContext(), cancellationToken).ConfigureAwait(false);
                })));

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

        var alias = registry.GetAlias(typeof(TEvent), listener.GetType());
        var job = new QueuedEventListenerJob
        {
            ListenerAlias = alias,
            EventPayload = JsonSerializer.Serialize(evt)
        };

        return dispatcher.DispatchAsync(job, cancellationToken: cancellationToken);
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
    Func<IServiceProvider, string, CancellationToken, Task> Execute);

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

    public string GetAlias(Type eventType, Type listenerType)
        => _byListener.TryGetValue((eventType, listenerType), out var registration)
            ? registration.Alias
            : throw new InvalidOperationException(
                $"Queued listener '{listenerType}' for event '{eventType}' has no explicit queue alias registration.");

    public Task ExecuteAsync(string alias, string payload, IServiceProvider provider, CancellationToken cancellationToken)
        => _byAlias.TryGetValue(alias, out var registration)
            ? registration.Execute(provider, payload, cancellationToken)
            : throw new InvalidOperationException($"Queued event listener alias '{alias}' is not registered.");
}
}
