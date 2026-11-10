using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

internal sealed class ThrottleMiddleware(
    ThrottleGuard guard,
    IOptions<RoutingOptions> options) : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var (name, policy) = ResolvePolicy(arguments, options.Value);
        var attempt = await guard
            .AttemptAsync(GetPartitionKey(context, name), name, policy, context.RequestAborted)
            .ConfigureAwait(false);

        if (attempt.Kind == ThrottleAttemptKind.Unlimited)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (attempt.Kind == ThrottleAttemptKind.Unavailable)
        {
            context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(attempt.RetryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        var decision = attempt.Decision;

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
        return PartitionKey(policyName, endpoint, subject);
    }

    /// <summary>
    /// The partition of one attempt. Each of the policy name, the endpoint and the subject is written as its length (in UTF-16
    /// code units), a colon and the text, and the three are joined with <c>|</c>, for example
    /// <c>3:5:2|8:/limited|11:203.0.113.7</c>. The length lets the text be read back in one way only, so a name that itself
    /// contains <c>|</c> or <c>:</c> cannot make two different triples give the same key (AUDIT D-87).
    /// </summary>
    internal static string PartitionKey(string policyName, string endpoint, string subject) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{policyName.Length}:{policyName}|{endpoint.Length}:{endpoint}|{subject.Length}:{subject}");
}
