using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

/// <summary>
/// Resolves route parameters that have a registered binding (<c>options.Bind&lt;User&gt;("user", ...)</c>) into models, or returns 404.
/// Registered under the alias <c>bindings</c>. Read the model with <c>context.GetRouteModel&lt;User&gt;("user")</c>.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>SubstituteBindings</c> (explicit <c>Route::bind</c> / <c>Route::model</c>).</para>
/// <para><b>Why not implicit binding:</b> Laravel's implicit binding reads type hints by reflection (a PHP mechanic). In .NET a
/// Minimal API parameter type can already bind itself with a static <c>BindAsync</c>/<c>TryParse</c>, so that stays native.</para>
/// <para><b>Not ported:</b> custom binding keys (<c>{user:slug}</c>), soft-deleted lookups, scoped child bindings.</para>
/// </remarks>
public sealed class SubstituteBindings(IOptions<RoutingOptions> options) : IRouteMiddleware
{
    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        foreach (var (name, value) in context.Request.RouteValues)
        {
            if (value is null || !options.Value.TryGetBinding(name, out var resolve)) continue;

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var model = await resolve(text, context);
            if (model is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Items[RouteBindingKeys.For(name)] = model;
        }

        await next(context);
    }
}

internal static class RouteBindingKeys
{
    public static string For(string parameter) => "naravel.binding:" + parameter.ToLowerInvariant();
}
