using Microsoft.AspNetCore.Builder;

namespace Naravel.Routing;

/// <summary>
/// A route being declared inside <c>MapNaravel</c>. Chain <c>Name</c>, <c>Middleware</c>, <c>WithoutMiddleware</c>,
/// <c>Where</c> and <c>Domain</c> on it. Nothing is mapped until the <c>MapNaravel</c> callback returns,
/// so the order of these calls does not matter.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Routing\Route</c> as returned by <c>Route::get()</c>.</para>
/// <para><b>Why it exists:</b> <see cref="RouteHandlerBuilder"/> cannot change the route pattern after creation, which <c>where()</c> needs.</para>
/// <para><b>Not ported:</b> <c>->uses()</c>/string controller actions, <c>->scopeBindings()</c>, <c>->missing()</c>, <c>->can()</c>.
/// Use <see cref="Configure"/> to reach the native <see cref="RouteHandlerBuilder"/> for OpenAPI metadata, filters and so on.</para>
/// </remarks>
public sealed class NaravelRoute
{
    private readonly List<MiddlewareSpec> _middleware = new();
    private readonly List<MiddlewareSpec> _without = new();
    private readonly Dictionary<string, string> _wheres = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Action<RouteHandlerBuilder>> _customizations = new();
    private readonly GroupState _group;

    internal NaravelRoute(IReadOnlyList<string> methods, string uri, Delegate handler, GroupState group, bool isFallback)
    {
        Methods = methods;
        Uri = uri;
        Handler = handler;
        _group = group;
        IsFallback = isFallback;
        Host = group.Domain;
    }

    /// <summary>HTTP methods this route answers.</summary>
    public IReadOnlyList<string> Methods { get; }

    /// <summary>The full URI pattern including group prefixes, starting with <c>/</c>.</summary>
    public string Uri { get; }

    /// <summary>The route name including group name prefixes, or <c>null</c>.</summary>
    public string? RouteName { get; private set; }

    /// <summary>The host restriction (from <see cref="Domain"/> or the enclosing group), or <c>null</c>.</summary>
    public string? Host { get; private set; }

    internal Delegate Handler { get; }

    internal bool IsFallback { get; }

    internal IReadOnlyDictionary<string, string> Wheres => _wheres;

    internal IReadOnlyList<Action<RouteHandlerBuilder>> Customizations => _customizations;

    internal IReadOnlyList<MiddlewareSpec> AllMiddleware => _group.Middleware.Concat(_middleware).ToList();

    internal IReadOnlyList<MiddlewareSpec> AllWithout => _group.Without.Concat(_without).ToList();

    /// <summary>Names the route; enclosing group name prefixes are prepended (<c>admin.</c> + <c>users</c> = <c>admin.users</c>).</summary>
    public NaravelRoute Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        RouteName = _group.NamePrefix + name;
        return this;
    }

    /// <summary>Attaches middleware by alias or group name, e.g. <c>"auth"</c>, <c>"throttle:60,1"</c>.</summary>
    public NaravelRoute Middleware(params string[] names)
    {
        foreach (var name in names) _middleware.Add(MiddlewareSpec.Named(name));
        return this;
    }

    /// <summary>Attaches a middleware type (must implement <see cref="IRouteMiddleware"/> or ASP.NET Core's <c>IMiddleware</c>).</summary>
    public NaravelRoute Middleware(Type type, params string[] arguments)
    {
        _middleware.Add(MiddlewareSpec.OfType(type, arguments));
        return this;
    }

    /// <summary>Attaches an inline middleware.</summary>
    public NaravelRoute Middleware(RouteMiddlewareDelegate middleware)
    {
        _middleware.Add(MiddlewareSpec.FromDelegate(middleware));
        return this;
    }

    /// <summary>Excludes middleware (including ones that came from an enclosing group) by alias or group name.</summary>
    public NaravelRoute WithoutMiddleware(params string[] names)
    {
        foreach (var name in names) _without.Add(MiddlewareSpec.Named(name));
        return this;
    }

    /// <summary>Excludes a middleware type.</summary>
    public NaravelRoute WithoutMiddleware(Type type)
    {
        _without.Add(MiddlewareSpec.OfType(type));
        return this;
    }

    /// <summary>Constrains a route parameter with a regular expression (anchored automatically).</summary>
    public NaravelRoute Where(string parameter, string regex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(regex);
        _wheres[parameter] = regex;
        return this;
    }

    /// <summary>Restricts the route to a host, e.g. <c>"api.example.com"</c> or <c>"*.example.com"</c>.</summary>
    public NaravelRoute Domain(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        Host = host;
        return this;
    }

    /// <summary>Gives access to the native <see cref="RouteHandlerBuilder"/> once the route is mapped.</summary>
    public NaravelRoute Configure(Action<RouteHandlerBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _customizations.Add(configure);
        return this;
    }
}

internal sealed record GroupState(
    string Prefix,
    string NamePrefix,
    string? Domain,
    IReadOnlyList<MiddlewareSpec> Middleware,
    IReadOnlyList<MiddlewareSpec> Without)
{
    public static GroupState Root { get; } =
        new("", "", null, Array.Empty<MiddlewareSpec>(), Array.Empty<MiddlewareSpec>());

    public GroupState WithPrefix(string prefix) => this with { Prefix = UriUtil.Join(Prefix, prefix) };

    public GroupState WithNamePrefix(string name) => this with { NamePrefix = NamePrefix + name };

    public GroupState WithDomain(string domain) => this with { Domain = domain };

    public GroupState WithMiddleware(IEnumerable<MiddlewareSpec> specs) => this with { Middleware = Middleware.Concat(specs).ToList() };

    public GroupState WithExcluded(IEnumerable<MiddlewareSpec> specs) => this with { Without = Without.Concat(specs).ToList() };
}

internal static class UriUtil
{
    public static string Join(string left, string right)
    {
        var l = left.Trim('/');
        var r = right.Trim('/');
        if (l.Length == 0) return r;
        if (r.Length == 0) return l;
        return l + "/" + r;
    }
}
