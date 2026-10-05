using Microsoft.AspNetCore.Http;

namespace Naravel.Routing;

/// <summary>
/// Options for <c>AddNaravelRouting</c>: the middleware registry, global parameter patterns and route model bindings.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the middleware parts of <c>bootstrap/app.php</c> / <c>Kernel.php</c>, <c>Route::pattern()</c> and <c>Route::bind()</c>.</para>
/// <para><b>Not ported:</b> a config file; routing setup is code, read once at startup. Unlike driver modules this is not
/// <c>ManagerOptions</c> and has no hot reload (see PDR-009).</para>
/// </remarks>
public sealed class RoutingOptions
{
    private readonly Dictionary<string, Func<string, HttpContext, ValueTask<object?>>> _bindings =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the options and registers the built-in <c>bindings</c> alias.</summary>
    public RoutingOptions()
    {
        Middleware.Alias("bindings", typeof(SubstituteBindings));
    }

    /// <summary>Aliases, groups and priority of route middleware.</summary>
    public MiddlewareOptions Middleware { get; } = new();

    /// <summary>Global regex constraints applied to every route parameter with that name (<c>Route::pattern</c>).</summary>
    public IDictionary<string, string> Patterns { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets a global regex constraint for a route parameter name.</summary>
    public RoutingOptions Pattern(string parameter, string regex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(regex);
        Patterns[parameter] = regex;
        return this;
    }

    /// <summary>
    /// Registers a route model binding. The <c>bindings</c> middleware calls <paramref name="resolver"/> with the raw parameter
    /// value; returning <c>null</c> makes the request a 404.
    /// </summary>
    public RoutingOptions Bind<TModel>(string parameter, Func<string, HttpContext, ValueTask<TModel?>> resolver) where TModel : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        ArgumentNullException.ThrowIfNull(resolver);
        _bindings[parameter] = async (value, context) => await resolver(value, context);
        return this;
    }

    internal bool TryGetBinding(string parameter, out Func<string, HttpContext, ValueTask<object?>> resolver) =>
        _bindings.TryGetValue(parameter, out resolver!);
}
