using Microsoft.AspNetCore.Http;

namespace Naravel.Routing;

internal sealed class RouteRegistrar : IRouteRegistrar
{
    private static readonly string[] GetMethods = { "GET", "HEAD" };
    private static readonly string[] AllMethods = { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS" };

    private readonly GroupState _state;

    public RouteRegistrar(GroupState state, List<NaravelRoute> routes)
    {
        _state = state;
        Routes = routes;
    }

    public List<NaravelRoute> Routes { get; }

    public NaravelRoute Get(string uri, Delegate handler) => Add(GetMethods, uri, handler);

    public NaravelRoute Post(string uri, Delegate handler) => Add(new[] { "POST" }, uri, handler);

    public NaravelRoute Put(string uri, Delegate handler) => Add(new[] { "PUT" }, uri, handler);

    public NaravelRoute Patch(string uri, Delegate handler) => Add(new[] { "PATCH" }, uri, handler);

    public NaravelRoute Delete(string uri, Delegate handler) => Add(new[] { "DELETE" }, uri, handler);

    public NaravelRoute Options(string uri, Delegate handler) => Add(new[] { "OPTIONS" }, uri, handler);

    public NaravelRoute Match(IEnumerable<string> methods, string uri, Delegate handler) =>
        Add(methods.Select(m => m.ToUpperInvariant()).Distinct().ToArray(), uri, handler);

    public NaravelRoute Any(string uri, Delegate handler) => Add(AllMethods, uri, handler);

    public NaravelRoute Fallback(Delegate handler) => Add(AllMethods, "{*fallbackPath}", handler, isFallback: true);

    public NaravelRoute Redirect(string uri, string destination, bool permanent = false)
    {
        Func<IResult> redirect = () => Results.Redirect(destination, permanent);
        return Add(AllMethods, uri, redirect);
    }

    public void Resource(string name, ResourceHandlers handlers, Action<ResourceOptions>? configure = null) =>
        MapResource(name, handlers, configure, api: false);

    public void ApiResource(string name, ResourceHandlers handlers, Action<ResourceOptions>? configure = null) =>
        MapResource(name, handlers, configure, api: true);

    public void Group(Action<IRouteRegistrar> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes(new RouteRegistrar(_state, Routes));
    }

    public PendingRouteGroup Prefix(string prefix) => new PendingRouteGroup(this, _state).Prefix(prefix);

    public PendingRouteGroup Name(string namePrefix) => new PendingRouteGroup(this, _state).Name(namePrefix);

    public PendingRouteGroup Domain(string host) => new PendingRouteGroup(this, _state).Domain(host);

    public PendingRouteGroup Middleware(params string[] names) => new PendingRouteGroup(this, _state).Middleware(names);

    public PendingRouteGroup WithoutMiddleware(params string[] names) => new PendingRouteGroup(this, _state).WithoutMiddleware(names);

    internal RouteRegistrar Child(GroupState state) => new(state, Routes);

    private NaravelRoute Add(IReadOnlyList<string> methods, string uri, Delegate handler, bool isFallback = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        ArgumentNullException.ThrowIfNull(handler);
        var route = new NaravelRoute(methods, "/" + UriUtil.Join(_state.Prefix, uri), handler, _state, isFallback);
        Routes.Add(route);
        return route;
    }

    private void MapResource(string name, ResourceHandlers handlers, Action<ResourceOptions>? configure, bool api)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handlers);

        var options = new ResourceOptions();
        configure?.Invoke(options);

        var uri = name.Trim('/');
        var parameter = options.Parameter ?? Singularize(uri.Split('/').Last());
        var nameBase = uri.Replace('/', '.');

        void Register(string action, string[] methods, string path, Delegate? handler)
        {
            if (handler is null) return;
            if (api && (action == "create" || action == "edit")) return;
            if (options.Only is not null && !options.Only.Contains(action, StringComparer.OrdinalIgnoreCase)) return;
            if (options.Except is not null && options.Except.Contains(action, StringComparer.OrdinalIgnoreCase)) return;
            Add(methods, path, handler).Name($"{nameBase}.{action}");
        }

        var item = $"{uri}/{{{parameter}}}";
        Register("index", GetMethods, uri, handlers.Index);
        Register("create", GetMethods, $"{uri}/create", handlers.Create);
        Register("store", new[] { "POST" }, uri, handlers.Store);
        Register("show", GetMethods, item, handlers.Show);
        Register("edit", GetMethods, $"{item}/edit", handlers.Edit);
        Register("update", new[] { "PUT", "PATCH" }, item, handlers.Update);
        Register("destroy", new[] { "DELETE" }, item, handlers.Destroy);
    }

    private static string Singularize(string word)
    {
        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && word.Length > 3) return word[..^3] + "y";
        if (word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) || word.EndsWith("us", StringComparison.OrdinalIgnoreCase)) return word;
        if (word.EndsWith('s') && word.Length > 1) return word[..^1];
        return word;
    }
}

/// <summary>
/// A group that has settings (prefix, name, domain, middleware) but no routes yet. Call <see cref="Group"/> to declare them.
/// </summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Routing\RouteRegistrar</c> (the chain before <c>->group()</c>).</remarks>
public sealed class PendingRouteGroup
{
    private readonly RouteRegistrar _parent;
    private GroupState _state;

    internal PendingRouteGroup(RouteRegistrar parent, GroupState state)
    {
        _parent = parent;
        _state = state;
    }

    /// <summary>Adds a URI prefix.</summary>
    public PendingRouteGroup Prefix(string prefix)
    {
        _state = _state.WithPrefix(prefix);
        return this;
    }

    /// <summary>Adds a route-name prefix.</summary>
    public PendingRouteGroup Name(string namePrefix)
    {
        _state = _state.WithNamePrefix(namePrefix);
        return this;
    }

    /// <summary>Restricts the group to a host.</summary>
    public PendingRouteGroup Domain(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _state = _state.WithDomain(host);
        return this;
    }

    /// <summary>Adds middleware by alias or group name.</summary>
    public PendingRouteGroup Middleware(params string[] names)
    {
        _state = _state.WithMiddleware(names.Select(MiddlewareSpec.Named));
        return this;
    }

    /// <summary>Excludes middleware for every route in the group.</summary>
    public PendingRouteGroup WithoutMiddleware(params string[] names)
    {
        _state = _state.WithExcluded(names.Select(MiddlewareSpec.Named));
        return this;
    }

    /// <summary>Declares the routes of the group.</summary>
    public void Group(Action<IRouteRegistrar> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        routes(_parent.Child(_state));
    }
}
