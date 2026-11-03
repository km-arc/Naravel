using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

internal sealed class ThrottleMiddleware(
    IThrottleStore store,
    IOptions<RoutingOptions> options) : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var (name, policy) = ResolvePolicy(arguments, options.Value);
        var decision = await store
            .AttemptAsync(GetPartitionKey(context, name), policy.PermitLimit, policy.Window, context.RequestAborted)
            .ConfigureAwait(false);

        context.Response.Headers["X-RateLimit-Limit"] = policy.PermitLimit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, decision.Remaining).ToString(CultureInfo.InvariantCulture);

        if (decision.Allowed)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(decision.RetryAfter.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    }

    private static (string Name, RoutingOptions.ThrottlePolicy Policy) ResolvePolicy(
        MiddlewareArguments arguments,
        RoutingOptions options)
    {
        if (arguments.Count == 1 && options.TryGetThrottlePolicy(arguments[0], out var named))
        {
            return (arguments[0], named);
        }

        if (arguments.Count == 2 &&
            int.TryParse(arguments[0], NumberStyles.None, CultureInfo.InvariantCulture, out var permitLimit) &&
            int.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var windowMinutes) &&
            permitLimit > 0 && windowMinutes > 0)
        {
            return ($"{permitLimit}:{windowMinutes}", new RoutingOptions.ThrottlePolicy(permitLimit, TimeSpan.FromMinutes(windowMinutes)));
        }

        throw new InvalidOperationException("Throttle middleware expects a configured policy name or positive permit-limit and window-minute arguments.");
    }

    private static string GetPartitionKey(HttpContext context, string policyName)
    {
        var principal = context.User;
        var subject = principal.Identity?.IsAuthenticated == true
            ? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value ?? principal.Identity.Name
            : null;
        subject ??= context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            ?? context.GetEndpoint()?.DisplayName
            ?? context.Request.Path.Value
            ?? "/";
        return $"{policyName}|{endpoint}|{subject}";
    }
}
