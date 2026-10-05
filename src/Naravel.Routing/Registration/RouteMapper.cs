using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Naravel.Routing;

internal static class RouteMapper
{
    public static void Map(
        IEndpointRouteBuilder endpoints,
        IEnumerable<NaravelRoute> routes,
        RoutingOptions options,
        MiddlewarePipelineFactory factory)
    {
        foreach (var route in routes)
        {
            var specs = route.AllMiddleware;
            var without = route.AllWithout;

            // Fail fast at startup: an unknown alias must not wait for the first request to be reported.
            factory.Resolve(specs, without);

            RouteHandlerBuilder builder;
            if (route.IsFallback)
            {
                builder = endpoints.MapFallback(route.Uri, route.Handler);
            }
            else
            {
                builder = endpoints.Map(BuildPattern(route, options), route.Handler);
                builder.WithMetadata(new HttpMethodMetadata(route.Methods));
            }

            if (route.RouteName is not null) builder.WithName(route.RouteName);
            if (route.Host is not null) builder.RequireHost(route.Host);
            if (specs.Count > 0 || without.Count > 0) builder.WithMetadata(new RouteMiddlewareMetadata(specs, without));

            foreach (var customize in route.Customizations) customize(builder);
        }
    }

    private static RoutePattern BuildPattern(NaravelRoute route, RoutingOptions options)
    {
        var parsed = RoutePatternFactory.Parse(route.Uri);
        var names = parsed.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in route.Wheres.Keys)
        {
            if (!names.Contains(parameter))
            {
                throw new InvalidOperationException(
                    $"Route '{route.Uri}' has Where(\"{parameter}\", ...) but no {{{parameter}}} parameter.");
            }
        }

        var policies = new RouteValueDictionary();
        foreach (var name in names)
        {
            if (route.Wheres.TryGetValue(name, out var regex) || options.Patterns.TryGetValue(name, out regex))
            {
                // RegexRouteConstraint does not anchor the expression itself, so we do (like the inline regex() constraint).
                policies[name] = new RegexRouteConstraint("^(" + regex + ")$");
            }
        }

        return policies.Count == 0
            ? parsed
            : RoutePatternFactory.Parse(route.Uri, (object?)null, (object?)policies);
    }
}
