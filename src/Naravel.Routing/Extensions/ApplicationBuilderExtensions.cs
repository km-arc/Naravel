using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Pipeline and endpoint registration for <c>Naravel.Routing</c>.</summary>
public static class NaravelRoutingApplicationBuilderExtensions
{
    /// <summary>Maps the seven conventional resource routes to actions on an MVC controller.</summary>
    /// <typeparam name="TController">The controller type containing the conventional resource actions.</typeparam>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="name">Resource path and route-name prefix, for example <c>photos</c>.</param>
    /// <param name="parameter">The item route parameter name. Defaults to <c>id</c>.</param>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> resource-controller route registration.</para>
    /// <para><b>Why it exists:</b> maps conventional MVC actions to named REST routes while reusing ASP.NET Core MVC.</para>
    /// <para><b>Not ported:</b> custom controller route templates and implicit action discovery.</para>
    /// <para>Actions are named <c>Index</c>, <c>Create</c>, <c>Store</c>, <c>Show</c>, <c>Edit</c>, <c>Update</c>, and <c>Destroy</c>.</para>
    /// </remarks>
    public static IEndpointRouteBuilder MapNaravelControllerResource<TController>(
        this IEndpointRouteBuilder endpoints,
        string name,
        string parameter = "id")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);

        var resource = name.Trim('/');
        if (resource.Length == 0 || resource.Contains('{') || resource.Contains('}'))
        {
            throw new ArgumentException("The resource name must be a non-empty route path without route parameters.", nameof(name));
        }

        if (!typeof(ControllerBase).IsAssignableFrom(typeof(TController)))
        {
            throw new ArgumentException("The resource type must derive from ControllerBase.", nameof(TController));
        }

        var controllerName = typeof(TController).Name;
        if (!controllerName.EndsWith("Controller", StringComparison.Ordinal))
        {
            throw new ArgumentException("The resource type must use the MVC Controller suffix.", nameof(TController));
        }

        controllerName = controllerName[..^"Controller".Length];
        var namePrefix = resource.Replace('/', '.');
        var item = $"{resource}/{{{parameter}}}";

        Map("index", "Index", resource, "GET", "HEAD");
        Map("create", "Create", $"{resource}/create", "GET", "HEAD");
        Map("store", "Store", resource, "POST");
        Map("show", "Show", item, "GET", "HEAD");
        Map("edit", "Edit", $"{item}/edit", "GET", "HEAD");
        Map("update", "Update", item, "PUT", "PATCH");
        Map("destroy", "Destroy", item, "DELETE");
        return endpoints;

        void Map(string routeAction, string controllerAction, string pattern, params string[] methods)
        {
            endpoints.MapControllerRoute(
                    $"{namePrefix}.{routeAction}",
                    pattern,
                    new { controller = controllerName, action = controllerAction })
                .WithMetadata(new HttpMethodMetadata(methods));
        }
    }

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
