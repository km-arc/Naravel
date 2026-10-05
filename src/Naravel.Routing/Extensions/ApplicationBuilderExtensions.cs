using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Pipeline and endpoint registration for <c>Naravel.Routing</c>.</summary>
public static class NaravelRoutingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware that runs each matched endpoint's route middleware. Call it after <c>UseRouting()</c>
    /// (with <c>WebApplication</c> routing is added automatically before your middleware).
    /// </summary>
    public static IApplicationBuilder UseNaravelRouting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<NaravelRoutingMiddleware>();
    }

    /// <summary>
    /// Declares routes with the Laravel-style registrar. Routes are mapped when <paramref name="routes"/> returns; an unknown
    /// middleware alias or a <c>Where</c> for a missing parameter throws here, at startup.
    /// </summary>
    public static IEndpointRouteBuilder MapNaravel(this IEndpointRouteBuilder endpoints, Action<IRouteRegistrar> routes)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(routes);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<RoutingOptions>>().Value;
        var factory = endpoints.ServiceProvider.GetRequiredService<MiddlewarePipelineFactory>();

        var collected = new List<NaravelRoute>();
        routes(new RouteRegistrar(GroupState.Root, collected));
        RouteMapper.Map(endpoints, collected, options, factory);
        return endpoints;
    }

    /// <summary>
    /// Attaches Naravel route middleware (by alias or group name) to any native endpoint, e.g. <c>app.MapGet(...).WithNaravelMiddleware("auth")</c>.
    /// </summary>
    public static TBuilder WithNaravelMiddleware<TBuilder>(this TBuilder builder, params string[] names)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var specs = names.Select(MiddlewareSpec.Named).ToList();
        builder.Add(endpoint => endpoint.Metadata.Add(new RouteMiddlewareMetadata(specs, Array.Empty<MiddlewareSpec>())));
        return builder;
    }

    /// <summary>Excludes Naravel route middleware (by alias or group name) on a native endpoint or group.</summary>
    public static TBuilder WithoutNaravelMiddleware<TBuilder>(this TBuilder builder, params string[] names)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        var specs = names.Select(MiddlewareSpec.Named).ToList();
        builder.Add(endpoint => endpoint.Metadata.Add(new RouteMiddlewareMetadata(Array.Empty<MiddlewareSpec>(), specs)));
        return builder;
    }
}
