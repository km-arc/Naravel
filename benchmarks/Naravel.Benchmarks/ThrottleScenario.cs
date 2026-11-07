using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Routing;

namespace Naravel.Benchmarks;

/// <summary>
/// One request, from one client address, through the route middleware pipeline of an endpoint that has the <c>throttle</c>
/// middleware. The benchmarks of the throttle (R31.T05, PDR-024) all measure this, so the only thing that differs between them is the
/// store the throttle counts in.
/// </summary>
/// <remarks>
/// This file uses only API that has existed since the throttle was first built (PDR-009), and nothing from the shared store, so the
/// same file compiles against a commit from before R31.T01. That is how the in-process number can be taken before and after the
/// <c>IThrottleStore</c> seam and compared.
/// </remarks>
internal sealed class ThrottleScenario : IDisposable
{
    // The limit is far above what any run sends, so every request is admitted: the benchmark measures the admitted path. A policy that
    // started to reject would measure the 429 path, which is another thing.
    private const string Policy = "throttle:1000000000,1";

    private static readonly IPAddress Subject = IPAddress.Parse("203.0.113.7");

    private readonly ServiceProvider _services;
    private readonly Endpoint _endpoint;
    private readonly NaravelRoutingMiddleware _middleware;

    /// <param name="configure">Extra registrations after <c>AddNaravelRouting</c>; null keeps the default in-process store.</param>
    public ThrottleScenario(Action<IServiceCollection>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelRouting();
        configure?.Invoke(services);
        _services = services.BuildServiceProvider();

        var throttle = MiddlewareSpec.Named(Policy);
        _endpoint = new Endpoint(
            static _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RouteMiddlewareMetadata([throttle], Array.Empty<MiddlewareSpec>())),
            "benchmark-throttled-route");
        _middleware = new NaravelRoutingMiddleware(
            static _ => Task.CompletedTask,
            _services.GetRequiredService<MiddlewarePipelineFactory>());
    }

    public IServiceProvider Services => _services;

    public Task InvokeAsync()
    {
        var context = new DefaultHttpContext { RequestServices = _services };
        context.Connection.RemoteIpAddress = Subject;
        context.SetEndpoint(_endpoint);
        return _middleware.InvokeAsync(context);
    }

    public void Dispose() => _services.Dispose();
}
