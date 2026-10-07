using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Routing;

namespace Naravel.Benchmarks;

[MemoryDiagnoser]
public class RoutingBenchmarks
{
    private ServiceProvider _services = null!;
    private Endpoint _endpoint = null!;
    private NaravelRoutingMiddleware _middleware = null!;

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddNaravelRouting();
        _services = services.BuildServiceProvider();

        var passThrough = MiddlewareSpec.FromDelegate(static (context, next, _) => next(context));
        _endpoint = new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RouteMiddlewareMetadata([passThrough], Array.Empty<MiddlewareSpec>())),
            "benchmark-route");
        _middleware = new NaravelRoutingMiddleware(
            static _ => Task.CompletedTask,
            _services.GetRequiredService<MiddlewarePipelineFactory>());
    }

    [Benchmark]
    public Task InvokeRouteMiddlewarePipeline()
    {
        var context = new DefaultHttpContext { RequestServices = _services };
        context.SetEndpoint(_endpoint);
        return _middleware.InvokeAsync(context);
    }

    [GlobalCleanup]
    public void Cleanup() => _services.Dispose();
}