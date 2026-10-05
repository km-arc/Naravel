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

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new Subscription(() => _listeners.Remove(listener));
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

internal static class TestKit
{
    public static readonly IServiceProvider EmptyProvider = new ServiceCollection().BuildServiceProvider();

    /// <summary>Builds options whose store sections come from a real in-memory configuration.</summary>
    public static ManagerOptions Options(string @default, params (string Store, string Key, string Value)[] values)
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
