using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Routing;

/// <summary>
/// One concrete step of a route's middleware pipeline, produced by <see cref="MiddlewarePipelineFactory"/> after aliases, groups,
/// exclusions, de-duplication and priority were applied.
/// </summary>
public sealed class ResolvedMiddleware
{
    private readonly RouteMiddlewareDelegate? _delegate;
    private readonly Lazy<ObjectFactory>? _factory;

    private ResolvedMiddleware(object identity, string displayName, Type? middlewareType, RouteMiddlewareDelegate? inline, MiddlewareArguments arguments)
    {
        Identity = identity;
        DisplayName = displayName;
        Type = middlewareType;
        _delegate = inline;
        Arguments = arguments;
        if (middlewareType is not null)
        {
            _factory = new Lazy<ObjectFactory>(() => ActivatorUtilities.CreateFactory(middlewareType, global::System.Type.EmptyTypes));
        }
    }

    internal object Identity { get; }

    /// <summary>Readable name for diagnostics (alias or type name).</summary>
    public string DisplayName { get; }

    /// <summary>The middleware type, or <c>null</c> for delegate-based middleware.</summary>
    public Type? Type { get; }

    /// <summary>The arguments the middleware receives.</summary>
    public MiddlewareArguments Arguments { get; }

    internal static ResolvedMiddleware ForType(Type type, IEnumerable<string> arguments, string displayName) =>
        new(type, displayName, type, null, new MiddlewareArguments(arguments));

    internal static ResolvedMiddleware ForDelegate(RouteMiddlewareDelegate inline, IEnumerable<string> arguments, object identity, string displayName) =>
        new(identity, displayName, null, inline, new MiddlewareArguments(arguments));

    internal RequestDelegate Bind(RequestDelegate next)
    {
        if (_delegate is not null)
        {
            var inline = _delegate;
            var inlineArguments = Arguments;
            return context => inline(context, next, inlineArguments);
        }

        var middlewareType = Type!;
        var factory = _factory!;
        var arguments = Arguments;
        return context =>
        {
            var services = context.RequestServices;
            var instance = services.GetService(middlewareType) ?? factory.Value(services, null);

            if (instance is ITerminableMiddleware terminable)
            {
                context.Response.OnCompleted(
                    static state =>
                    {
                        var (t, c, a) = ((ITerminableMiddleware, HttpContext, MiddlewareArguments))state;
                        return t.TerminateAsync(c, a);
                    },
                    (terminable, context, arguments));
            }

            return instance switch
            {
                IRouteMiddleware routeMiddleware => routeMiddleware.InvokeAsync(context, next, arguments),
                Microsoft.AspNetCore.Http.IMiddleware middleware => middleware.InvokeAsync(context, next),
                _ => throw new InvalidOperationException(
                    $"'{middlewareType.FullName}' does not implement IRouteMiddleware or Microsoft.AspNetCore.Http.IMiddleware."),
            };
        };
    }
}
