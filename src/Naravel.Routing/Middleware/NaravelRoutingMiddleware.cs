using Microsoft.AspNetCore.Http;

namespace Naravel.Routing;

/// <summary>
/// The single ASP.NET Core middleware that runs each matched endpoint's route middleware. Add it with <c>app.UseNaravelRouting()</c>
/// after routing (with <c>WebApplication</c> routing is added for you).
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Router::runRouteWithinStack</c> (the Pipeline that wraps the matched route).</para>
/// <para><b>Why it exists:</b> ASP.NET Core has no per-endpoint middleware pipeline outside MVC filters and endpoint filters.
/// This works for Minimal API and controllers alike, and resolves each endpoint's pipeline once.</para>
/// <para><b>Not ported:</b> Laravel's global middleware list; use plain <c>app.Use*</c>, which also runs for unmatched requests.</para>
/// </remarks>
public sealed class NaravelRoutingMiddleware(RequestDelegate next, MiddlewarePipelineFactory factory)
{
    /// <summary>Runs the route middleware of the matched endpoint, then the endpoint.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null) return next(context);

        var pipeline = factory.GetPipeline(endpoint);
        if (pipeline.Count == 0) return next(context);

        var current = next;
        for (var i = pipeline.Count - 1; i >= 0; i--) current = pipeline[i].Bind(current);
        return current(context);
    }
}
