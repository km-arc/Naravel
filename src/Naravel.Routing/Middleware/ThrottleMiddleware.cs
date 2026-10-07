using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Naravel.Routing;

internal sealed class ThrottleMiddleware(
    ThrottleLimiterRegistry limiters,
    IOptions<RoutingOptions> options) : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var (name, policy) = ResolvePolicy(arguments, options.Value);
        var limiter = limiters.Get(name, policy.PermitLimit, policy.Window);
        using var lease = await limiter.AcquireAsync(context, 1, context.RequestAborted).ConfigureAwait(false);
        var remaining = limiter.GetStatistics(context)?.CurrentAvailablePermits ?? 0;

        context.Response.Headers["X-RateLimit-Limit"] = policy.PermitLimit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, remaining).ToString(CultureInfo.InvariantCulture);

        if (lease.IsAcquired)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan value)
            ? value
            : policy.Window;
        context.Response.Headers.RetryAfter = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds))
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
}

internal sealed class ThrottleLimiterRegistry : IDisposable
{
    private readonly record struct LimiterKey(string Name, int PermitLimit, long WindowTicks);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<LimiterKey, PartitionedRateLimiter<HttpContext>> _limiters = new();

    public PartitionedRateLimiter<HttpContext> Get(string name, int permitLimit, TimeSpan window)
    {
        var key = new LimiterKey(name, permitLimit, window.Ticks);
        return _limiters.GetOrAdd(key, static limiterKey =>
            PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var partition = GetPartitionKey(context, limiterKey.Name);
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limiterKey.PermitLimit,
                    Window = TimeSpan.FromTicks(limiterKey.WindowTicks),
                    QueueLimit = 0,
                    AutoReplenishment = true
                });
            }));
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

    public void Dispose()
    {
        foreach (var limiter in _limiters.Values) limiter.Dispose();
        _limiters.Clear();
    }
}