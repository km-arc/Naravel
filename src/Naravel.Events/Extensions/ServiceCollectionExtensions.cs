using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration for <c>Naravel.Events</c>.
/// </summary>
public static class NaravelEventsServiceCollectionExtensions
{
    /// <summary>
    /// Adds a scoped event dispatcher and its direct listener invoker.
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> the event service provider.</para>
    /// <para><b>Why it exists:</b> it provides a concise, explicit registration point over .NET DI.</para>
    /// <para><b>Not ported on purpose:</b> global facades and automatic event/listener discovery.</para>
    /// </remarks>
    public static IServiceCollection AddNaravelEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<Naravel.Events.IEventDispatcher, Naravel.Events.EventDispatcher>();
        services.TryAddScoped<Naravel.Events.IEventListenerInvoker, DirectEventListenerInvoker>();
        return services;
    }
}

internal sealed class DirectEventListenerInvoker : Naravel.Events.IEventListenerInvoker
{
    public Task InvokeAsync<TEvent>(
        Naravel.Events.IEventListener<TEvent> listener,
        TEvent evt,
        Naravel.Events.EventDispatchContext context,
        CancellationToken cancellationToken)
        => listener.HandleAsync(evt, context, cancellationToken);
}
