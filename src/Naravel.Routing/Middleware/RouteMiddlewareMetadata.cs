namespace Naravel.Routing;

/// <summary>
/// Endpoint metadata holding the middleware attached to a route. Added by <c>MapNaravel</c> and by
/// <c>WithNaravelMiddleware</c>; read by <see cref="NaravelRoutingMiddleware"/>.
/// </summary>
public sealed class RouteMiddlewareMetadata
{
    /// <summary>Creates the metadata.</summary>
    public RouteMiddlewareMetadata(IReadOnlyList<MiddlewareSpec> middleware, IReadOnlyList<MiddlewareSpec> without)
    {
        Middleware = middleware;
        Without = without;
    }

    /// <summary>Middleware to run, in order.</summary>
    public IReadOnlyList<MiddlewareSpec> Middleware { get; }

    /// <summary>Middleware (or groups) to exclude.</summary>
    public IReadOnlyList<MiddlewareSpec> Without { get; }
}

/// <summary>
/// Attaches a route middleware to a controller or an action.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>HasMiddleware::middleware()</c> / <c>new Middleware('auth', only: [...], except: [...])</c>.</para>
/// <para><b>Why an attribute:</b> that is the idiomatic .NET way to attach behaviour to a controller; <see cref="Only"/> and
/// <see cref="Except"/> match action method names (case-insensitive).</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class MiddlewareAttribute(string name) : Attribute
{
    /// <summary>Alias or group name, optionally with arguments: <c>"throttle:60,1"</c>.</summary>
    public string Name { get; } = name;

    /// <summary>Apply only to these action names.</summary>
    public string[]? Only { get; set; }

    /// <summary>Do not apply to these action names.</summary>
    public string[]? Except { get; set; }

    internal bool AppliesTo(string? action) => ActionFilter.Applies(action, Only, Except);
}

/// <summary>
/// Excludes a middleware (or group) for a controller or action. <b>Laravel equivalent:</b> <c>withoutMiddleware()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class WithoutMiddlewareAttribute(string name) : Attribute
{
    /// <summary>Alias or group name to exclude.</summary>
    public string Name { get; } = name;

    /// <summary>Apply only to these action names.</summary>
    public string[]? Only { get; set; }

    /// <summary>Do not apply to these action names.</summary>
    public string[]? Except { get; set; }

    internal bool AppliesTo(string? action) => ActionFilter.Applies(action, Only, Except);
}

internal static class ActionFilter
{
    public static bool Applies(string? action, string[]? only, string[]? except)
    {
        if (action is null) return true;
        if (only is { Length: > 0 } && !only.Contains(action, StringComparer.OrdinalIgnoreCase)) return false;
        if (except is { Length: > 0 } && except.Contains(action, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }
}
