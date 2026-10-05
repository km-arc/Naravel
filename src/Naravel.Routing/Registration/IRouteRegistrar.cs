namespace Naravel.Routing;

/// <summary>
/// Declares routes inside <c>app.MapNaravel(r =&gt; ...)</c>: verbs, groups, fallback, redirect and resources.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>Route</c> facade / <c>Illuminate\Routing\Router</c>.</para>
/// <para><b>Why it is not a static facade:</b> facades are an open decision (see the parity table); the registrar is handed to you by
/// <c>MapNaravel</c>, so nothing is global and everything is testable.</para>
/// <para><b>Not ported:</b> <c>Route::view</c> (no Blade), string controller actions, <c>Route::current()</c>, <c>route:cache</c>.</para>
/// </remarks>
public interface IRouteRegistrar
{
    /// <summary>Registers a GET route (also answers HEAD, like Laravel).</summary>
    NaravelRoute Get(string uri, Delegate handler);

    /// <summary>Registers a POST route.</summary>
    NaravelRoute Post(string uri, Delegate handler);

    /// <summary>Registers a PUT route.</summary>
    NaravelRoute Put(string uri, Delegate handler);

    /// <summary>Registers a PATCH route.</summary>
    NaravelRoute Patch(string uri, Delegate handler);

    /// <summary>Registers a DELETE route.</summary>
    NaravelRoute Delete(string uri, Delegate handler);

    /// <summary>Registers an OPTIONS route.</summary>
    NaravelRoute Options(string uri, Delegate handler);

    /// <summary>Registers a route for the given HTTP methods.</summary>
    NaravelRoute Match(IEnumerable<string> methods, string uri, Delegate handler);

    /// <summary>Registers a route for GET, HEAD, POST, PUT, PATCH, DELETE and OPTIONS.</summary>
    NaravelRoute Any(string uri, Delegate handler);

    /// <summary>Registers the fallback route (matches anything else under the current group prefix).</summary>
    NaravelRoute Fallback(Delegate handler);

    /// <summary>Redirects <paramref name="uri"/> to <paramref name="destination"/> (302, or 301 when <paramref name="permanent"/>).</summary>
    NaravelRoute Redirect(string uri, string destination, bool permanent = false);

    /// <summary>Registers the seven conventional resource routes for the handlers that are set.</summary>
    void Resource(string name, ResourceHandlers handlers, Action<ResourceOptions>? configure = null);

    /// <summary>Like <see cref="Resource"/> without <c>create</c> and <c>edit</c>.</summary>
    void ApiResource(string name, ResourceHandlers handlers, Action<ResourceOptions>? configure = null);

    /// <summary>Runs <paramref name="routes"/> with a child registrar that shares the current group settings.</summary>
    void Group(Action<IRouteRegistrar> routes);

    /// <summary>Starts a group with a URI prefix.</summary>
    PendingRouteGroup Prefix(string prefix);

    /// <summary>Starts a group with a route-name prefix (e.g. <c>"admin."</c>).</summary>
    PendingRouteGroup Name(string namePrefix);

    /// <summary>Starts a group restricted to a host.</summary>
    PendingRouteGroup Domain(string host);

    /// <summary>Starts a group with middleware.</summary>
    PendingRouteGroup Middleware(params string[] names);

    /// <summary>Starts a group that excludes middleware.</summary>
    PendingRouteGroup WithoutMiddleware(params string[] names);
}

/// <summary>Handlers for <see cref="IRouteRegistrar.Resource"/>. Only the ones that are set produce routes.</summary>
/// <remarks><b>Laravel equivalent:</b> the methods of a resource controller (<c>index, create, store, show, edit, update, destroy</c>).</remarks>
public sealed class ResourceHandlers
{
    /// <summary>GET /name</summary>
    public Delegate? Index { get; set; }

    /// <summary>GET /name/create</summary>
    public Delegate? Create { get; set; }

    /// <summary>POST /name</summary>
    public Delegate? Store { get; set; }

    /// <summary>GET /name/{param}</summary>
    public Delegate? Show { get; set; }

    /// <summary>GET /name/{param}/edit</summary>
    public Delegate? Edit { get; set; }

    /// <summary>PUT|PATCH /name/{param}</summary>
    public Delegate? Update { get; set; }

    /// <summary>DELETE /name/{param}</summary>
    public Delegate? Destroy { get; set; }
}

/// <summary>Options for a resource registration.</summary>
public sealed class ResourceOptions
{
    /// <summary>The route parameter name. Default: the last URI segment made singular (<c>photos</c> gives <c>photo</c>).</summary>
    public string? Parameter { get; set; }

    /// <summary>Only these actions (<c>index, create, store, show, edit, update, destroy</c>).</summary>
    public string[]? Only { get; set; }

    /// <summary>All actions except these.</summary>
    public string[]? Except { get; set; }
}
