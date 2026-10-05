namespace Naravel.Routing;

/// <summary>
/// An unresolved reference to a middleware: an alias or group name (optionally with <c>:arguments</c>), a type, or an inline delegate.
/// It is resolved lazily so aliases can be configured independently of route registration order.
/// </summary>
/// <remarks><b>Laravel equivalent:</b> the strings and class names passed to <c>->middleware()</c>.</remarks>
public sealed class MiddlewareSpec
{
    private MiddlewareSpec(string? name, Type? type, RouteMiddlewareDelegate? inline, IReadOnlyList<string> arguments)
    {
        Name = name;
        Type = type;
        Inline = inline;
        Arguments = arguments;
    }

    /// <summary>Alias or group reference such as <c>throttle:60,1</c>; <c>null</c> for type or inline references.</summary>
    public string? Name { get; }

    /// <summary>The middleware type, for type references.</summary>
    public Type? Type { get; }

    /// <summary>The inline delegate, for inline references.</summary>
    public RouteMiddlewareDelegate? Inline { get; }

    /// <summary>Explicit arguments (type and inline references only; alias references carry theirs in <see cref="Name"/>).</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>Creates a reference by alias or group name, e.g. <c>"auth"</c> or <c>"throttle:60,1"</c>.</summary>
    public static MiddlewareSpec Named(string nameWithArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameWithArguments);
        return new MiddlewareSpec(nameWithArguments.Trim(), null, null, Array.Empty<string>());
    }

    /// <summary>Creates a reference by type. The type must implement <see cref="IRouteMiddleware"/> or ASP.NET Core's <c>IMiddleware</c>.</summary>
    public static MiddlewareSpec OfType(Type type, params string[] arguments)
    {
        MiddlewareTypes.Validate(type);
        return new MiddlewareSpec(null, type, null, arguments);
    }

    /// <summary>Creates a reference to an inline delegate.</summary>
    public static MiddlewareSpec FromDelegate(RouteMiddlewareDelegate middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        return new MiddlewareSpec(null, null, middleware, Array.Empty<string>());
    }

    /// <inheritdoc />
    public override string ToString() => Name ?? Type?.Name ?? "inline";
}

internal static class MiddlewareTypes
{
    public static void Validate(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var ok = !type.IsAbstract && !type.IsInterface &&
                 (typeof(IRouteMiddleware).IsAssignableFrom(type) || typeof(Microsoft.AspNetCore.Http.IMiddleware).IsAssignableFrom(type));
        if (!ok)
        {
            throw new ArgumentException(
                $"'{type.FullName}' must be a concrete class implementing IRouteMiddleware or Microsoft.AspNetCore.Http.IMiddleware.",
                nameof(type));
        }
    }
}
