using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Events;

namespace Naravel.Benchmarks;

public sealed record BenchmarkEvent(int Id);

public sealed class BenchmarkEventListener : IEventListener<BenchmarkEvent>
{
    public Task HandleAsync(BenchmarkEvent evt, EventDispatchContext context, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

[MemoryDiagnoser]
public class EventDispatchBenchmarks
{
    private ServiceProvider _provider = null!;
    private IServiceScope _scope = null!;
    private IEventDispatcher _dispatcher = null!;
    private readonly BenchmarkEvent _event = new(1);

    [GlobalSetup]
    public async Task Setup()
    {
        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddSingleton<IEventListener<BenchmarkEvent>, BenchmarkEventListener>();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();
        _dispatcher = _scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        await _dispatcher.DispatchAsync(_event);
    }

    [Benchmark]
    public Task DispatchWarmEvent() => _dispatcher.DispatchAsync(_event);

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }
}
