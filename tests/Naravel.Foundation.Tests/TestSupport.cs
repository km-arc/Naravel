using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Naravel.Foundation.Tests;

internal interface IFakeDriver
{
    string Id { get; }
}

internal class FakeDriver(string id) : IFakeDriver, IDisposable
{
    public string Id { get; } = id;

    public int DisposeCalls;

    public bool IsDisposed => DisposeCalls > 0;

    public void Dispose() => Interlocked.Increment(ref DisposeCalls);
}

internal sealed class AsyncFakeDriver(string id) : IFakeDriver, IAsyncDisposable
{
    public string Id { get; } = id;

    public int DisposeCalls;

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref DisposeCalls);
        return ValueTask.CompletedTask;
    }
}

internal sealed class ThrowingDriver(string id) : IFakeDriver, IDisposable
{
    public string Id { get; } = id;

    public void Dispose() => throw new InvalidOperationException("dispose-failed");
}

internal sealed class FakeManager(
    IServiceProvider provider,
    IDriverRegistry<IFakeDriver> registry,
    IOptionsMonitor<ManagerOptions> options) : Manager<IFakeDriver, ManagerOptions>(provider, registry, options);

/// <summary>Hand-driven IOptionsMonitor so tests control exactly when a "reload" happens.</summary>
internal sealed class FakeOptionsMonitor<T>(T initial) : IOptionsMonitor<T>
    where T : class
{
    private readonly List<Action<T, string?>> _listeners = new();

    public T CurrentValue { get; private set; } = initial;

    public int ListenerCount => _listeners.Count;

    /// <summary>When set, disposing the subscription that <see cref="OnChange"/> returned throws it.</summary>
    public Exception? DisposeFailure { get; set; }

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Subscription(() =>
        {
            if (DisposeFailure is not null)
            {
                throw DisposeFailure;
            }

            _listeners.Remove(listener);
        });
    }

    public void Publish(T value, string? name = "")
    {
        CurrentValue = value;
        foreach (var listener in _listeners.ToArray())
        {
            listener(value, name);
        }
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}

/// <summary>
/// Wraps a registry so that a test can act "on another thread" at an exact point of the manager's check-then-create sequence:
/// <see cref="AfterIsAsyncOnly"/> runs once, right after the manager has asked whether a driver is asynchronous-only, and
/// <see cref="BeforeCreate"/> runs once, right before the manager asks for a synchronous creation.
/// </summary>
internal sealed class InterceptingRegistry(DriverRegistry<IFakeDriver> inner) : IDriverRegistry<IFakeDriver>
{
    public Action<string>? AfterIsAsyncOnly { get; set; }

    public Action<string>? BeforeCreate { get; set; }

    /// <summary>When set, detaching from <see cref="Registered"/> (which the manager does when it is disposed) throws it.</summary>
    public Exception? UnsubscribeFailure { get; set; }

    public event Action<string>? Registered
    {
        add => inner.Registered += value;
        remove
        {
            if (UnsubscribeFailure is not null)
            {
                throw UnsubscribeFailure;
            }

            inner.Registered -= value;
        }
    }

    public IReadOnlyCollection<string> Names => inner.Names;

    public void Register(string name, Func<IServiceProvider, IFakeDriver> factory) => inner.Register(name, factory);

    public void Register(string name, Func<IServiceProvider, CancellationToken, ValueTask<IFakeDriver>> factory) =>
        inner.Register(name, factory);

    public bool IsRegistered(string name) => inner.IsRegistered(name);

    public bool IsAsyncOnly(string name)
    {
        var result = inner.IsAsyncOnly(name);
        var hook = AfterIsAsyncOnly;
        AfterIsAsyncOnly = null;
        hook?.Invoke(name);
        return result;
    }

    public IFakeDriver Create(string name, IServiceProvider provider)
    {
        var hook = BeforeCreate;
        BeforeCreate = null;
        hook?.Invoke(name);
        return inner.Create(name, provider);
    }

    public ValueTask<IFakeDriver> CreateAsync(string name, IServiceProvider provider, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(name, provider, cancellationToken);
}

internal static class TestKit
{
    public static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

    /// <summary>Builds options whose store sections come from a real in-memory configuration.</summary>
    /// <summary>Options whose store is written as one value (<c>Stores:name = value</c>) instead of as a section with keys.</summary>
    public static ManagerOptions SingleValueStore(string name, string value)
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [name] = value }).Build();
        var options = new ManagerOptions { Default = name };
        options.Stores[name] = root.GetSection(name);
        return options;
    }

    public static ManagerOptions Options(string @default, params (string Store, string Key, string? Value)[] values)
    {
        var data = new Dictionary<string, string?>();
        foreach (var (store, key, value) in values)
        {
            data[$"Stores:{store}:{key}"] = value;
        }

        var root = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var options = new ManagerOptions { Default = @default };
        foreach (var child in root.GetSection("Stores").GetChildren())
        {
            options.Stores[child.Key] = child;
        }

        return options;
    }
}

internal sealed class ManagerHarness
{
    public ManagerHarness(ManagerOptions? options = null)
    {
        Monitor = new FakeOptionsMonitor<ManagerOptions>(options ?? TestKit.Options("a"));
        Manager = new FakeManager(TestKit.EmptyProvider, Registry, Monitor);
    }

    public DriverRegistry<IFakeDriver> Registry { get; } = new();

    public FakeOptionsMonitor<ManagerOptions> Monitor { get; }

    public FakeManager Manager { get; }
}
