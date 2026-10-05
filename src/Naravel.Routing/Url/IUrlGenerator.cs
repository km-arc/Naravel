using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Naravel.Routing;

/// <summary>
/// Builds URLs from route names. Values that are not route parameters become the query string.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>route('name', [...])</c> helper / <c>URL::route()</c>.</para>
/// <para><b>Why it exists:</b> <see cref="LinkGenerator"/> already does the work; this adds the short Laravel call and a clear exception
/// (<see cref="RouteNotFoundException"/>) instead of a <c>null</c> return.</para>
/// <para><b>Not ported:</b> <c>URL::signedRoute</c> (planned for Stage 4b in <c>Naravel.Routing</c>), <c>URL::previous()</c>, <c>URL::action()</c>.</para>
/// </remarks>
public interface IUrlGenerator
{
    /// <summary>Returns the relative URL (path and query) of a named route.</summary>
    /// <exception cref="RouteNotFoundException">No route has that name, or required values are missing.</exception>
    string Route(string name, object? values = null);

    /// <summary>Returns the absolute URL of a named route using the scheme and host of <paramref name="context"/>.</summary>
    /// <exception cref="RouteNotFoundException">No route has that name, or required values are missing.</exception>
    string AbsoluteRoute(HttpContext context, string name, object? values = null);
}

/// <summary>Thrown when a route name cannot be turned into a URL.</summary>
public sealed class RouteNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public RouteNotFoundException(string name)
        : base($"No route named '{name}' could be generated. Check the name and that all required parameters were supplied.")
    {
        RouteName = name;
    }

    /// <summary>The requested route name.</summary>
    public string RouteName { get; }
}
