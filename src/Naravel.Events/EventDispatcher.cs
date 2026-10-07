using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Events;

/// <summary>
/// Default dispatcher that resolves listeners from DI and invokes them in registration order.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the concrete event dispatcher.</para>
/// <para><b>Why it exists:</b> it caches resolved listener plans without extending the service-provider lifetime.</para>
/// <para><b>Not ported on purpose:</b> reflection discovery, global facade behavior, and implicit subscriber methods.</para>
/// </remarks>
public sealed class EventDispatcher : IEventDispatcher
{
    private readonly IServiceProvider _provider;
    private readonly IEventListenerInvoker _listenerInvoker;
    private readonly ConcurrentDictionary<Type, Lazy<object>> _listenerPlans = new();
    private readonly Dictionary<Type, List<Subscription>> _subscriptions = new();
    private readonly object _subscriptionGate = new();

    /// <summary>
    /// Creates the scoped dispatcher using the current DI scope and configured listener invoker.
    /// </summary>
    public EventDispatcher(IServiceProvider provider, IEventListenerInvoker listenerInvoker)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(listenerInvoker);
        _provider = provider;
        _listenerInvoker = listenerInvoker;
    }

    /// <inheritdoc />
    public IDisposable Listen<TEvent>(Func<TEvent, EventDispatchContext, CancellationToken, Task> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        var subscription = new Subscription(listener);
        lock (_subscriptionGate)
        {
            if (!_subscriptions.TryGetValue(typeof(TEvent), out var listeners))
            {
                listeners = new List<Subscription>();
                _subscriptions[typeof(TEvent)] = listeners;
            }

            listeners.Add(subscription);
        }

        return new SubscriptionHandle(() => RemoveSubscription(typeof(TEvent), subscription));
    }

    /// <inheritdoc />
    public async Task DispatchAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(evt);

        using var activity = EventTelemetry.ActivitySource.StartActivity("events.dispatch", ActivityKind.Internal);
        var startedAt = Stopwatch.GetTimestamp();
        var completed = false;
        var context = new EventDispatchContext();
        try
        {
            var listeners = ResolveListeners<TEvent>();
            foreach (var listener in listeners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _listenerInvoker.InvokeAsync(listener, evt, context, cancellationToken).ConfigureAwait(false);
                if (context.StopPropagation)
                {
                    break;
                }
            }

            foreach (var subscription in context.StopPropagation ? Array.Empty<Subscription>() : SnapshotSubscriptions<TEvent>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await subscription.InvokeAsync(evt, context, cancellationToken).ConfigureAwait(false);
                if (context.StopPropagation)
                {
                    break;
                }
            }

            EventTelemetry.Dispatched.Add(1);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            EventTelemetry.Failed.Add(1);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            EventTelemetry.DispatchDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            if (completed)
            {
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
        }
    }

    private IEventListener<TEvent>[] ResolveListeners<TEvent>()
    {
        var plan = _listenerPlans.GetOrAdd(
            typeof(TEvent),
            _ => new Lazy<object>(
                () => _provider.GetServices<IEventListener<TEvent>>().ToArray(),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return (IEventListener<TEvent>[])plan.Value;
    }

    private IReadOnlyList<Subscription> SnapshotSubscriptions<TEvent>()
    {
        lock (_subscriptionGate)
        {
            return _subscriptions.TryGetValue(typeof(TEvent), out var listeners)
                ? listeners.ToArray()
                : Array.Empty<Subscription>();
        }
    }

    private void RemoveSubscription(Type eventType, Subscription subscription)
    {
        lock (_subscriptionGate)
        {
            if (_subscriptions.TryGetValue(eventType, out var listeners))
            {
                listeners.Remove(subscription);
                if (listeners.Count == 0)
                {
                    _subscriptions.Remove(eventType);
                }
            }
        }
    }

    private sealed record Subscription(Delegate Listener)
    {
        public Task InvokeAsync<TEvent>(TEvent evt, EventDispatchContext context, CancellationToken cancellationToken)
            => ((Func<TEvent, EventDispatchContext, CancellationToken, Task>)Listener)(evt, context, cancellationToken);
    }

    private sealed class SubscriptionHandle(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }
}
