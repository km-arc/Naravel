using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Naravel.Routing;

/// <summary>
/// A middleware that runs around a matched route. Register it under an alias
/// (<c>options.Middleware.Alias("auth", typeof(MyAuth))</c>) and attach it with <c>.Middleware("auth")</c>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> a class with <c>handle($request, Closure $next, ...$params)</c>.</para>
/// <para><b>Why it exists:</b> ASP.NET Core middleware is pipeline-wide and takes no per-route parameters;
/// this contract adds the <c>alias:arg1,arg2</c> parameters Laravel users expect, while staying a plain class resolved from DI.</para>
/// <para><b>Not ported:</b> the <c>$request</c> object (use <see cref="HttpContext"/>) and returning a response object
/// (write to <c>context.Response</c> or short-circuit by not invoking the delegate passed to <c>InvokeAsync</c>).</para>
/// </remarks>
public interface IRouteMiddleware
{
    /// <summary>Runs the middleware. Call <paramref name="next"/> to continue, or return without calling it to short-circuit.</summary>
    Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments);
}

/// <summary>
/// Optional add-on for an <see cref="IRouteMiddleware"/> that must do work after the response was sent.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>terminate($request, $response)</c> on a terminable middleware.</para>
/// <para><b>Implementation:</b> registered through <c>HttpResponse.OnCompleted</c>. ASP.NET Core runs those callbacks after the
/// response has been sent; when several middleware are terminable, their order is not guaranteed.</para>
/// </remarks>
public interface ITerminableMiddleware
{
    /// <summary>Called after the response was sent.</summary>
    Task TerminateAsync(HttpContext context, MiddlewareArguments arguments);
}

/// <summary>An inline (lambda) route middleware. See <see cref="IRouteMiddleware"/>.</summary>
public delegate Task RouteMiddlewareDelegate(HttpContext context, RequestDelegate next, MiddlewareArguments arguments);

/// <summary>
/// The parameters written after the colon of a middleware reference: <c>throttle:60,1</c> gives <c>["60", "1"]</c>.
/// </summary>
/// <remarks><b>Laravel equivalent:</b> the <c>...$params</c> of <c>handle()</c>. Values are always strings, as in Laravel.</remarks>
public sealed class MiddlewareArguments : IReadOnlyList<string>
{
    private readonly string[] _values;

    /// <summary>No parameters.</summary>
    public static MiddlewareArguments Empty { get; } = new(Array.Empty<string>());

    /// <summary>Creates the argument list.</summary>
    public MiddlewareArguments(IEnumerable<string> values) => _values = values.ToArray();

    /// <inheritdoc />
    public int Count => _values.Length;

    /// <inheritdoc />
    public string this[int index] => _values[index];

    /// <summary>Returns the argument at <paramref name="index"/>, or <c>null</c> when it was not given.</summary>
    public string? At(int index) => index >= 0 && index < _values.Length ? _values[index] : null;

    /// <summary>Returns the argument at <paramref name="index"/> as an integer, or <paramref name="fallback"/>.</summary>
    public int Int(int index, int fallback) =>
        int.TryParse(At(index), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}
