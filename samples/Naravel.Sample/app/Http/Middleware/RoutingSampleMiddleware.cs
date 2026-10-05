using Microsoft.AspNetCore.Http;
using Naravel.Routing;

namespace Naravel.Sample.App.Http.Middleware;

public sealed class ApiKeyMiddleware : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var headerName = arguments.At(0) ?? "X-Sample-Key";
        var expected = arguments.At(1) ?? "naravel-demo";
        if (!context.Request.Headers.TryGetValue(headerName, out var value) || value.ToString() != expected)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("A valid sample API key is required.");
            return;
        }

        await next(context);
    }
}

public sealed class RoleMiddleware : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        if (!context.Request.Headers.TryGetValue("X-Sample-Role", out var role) || role.ToString() != arguments.At(0))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("The required sample role is missing.");
            return;
        }

        await next(context);
    }
}

public sealed class AuditMiddleware : IRouteMiddleware, ITerminableMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        context.Response.Headers["X-Audit-Middleware"] = "entered";
        await next(context);
    }

    public Task TerminateAsync(HttpContext context, MiddlewareArguments arguments)
    {
        Console.WriteLine($"Terminated route audit for {context.Request.Path}.");
        return Task.CompletedTask;
    }
}

public sealed class TraceFirstMiddleware : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        RouteTrace.Add(context, "trace-first");
        await next(context);
    }
}

public sealed class TraceSecondMiddleware : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        RouteTrace.Add(context, "trace-second");
        await next(context);
    }
}

public sealed class StampMiddleware : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Stamp-Middleware"] = "active";
        return next(context);
    }
}

internal static class RouteTrace
{
    private const string Key = "Naravel.Sample.RouteTrace";

    public static void Add(HttpContext context, string value)
    {
        if (!context.Items.TryGetValue(Key, out var existing) || existing is not List<string> values)
        {
            values = new List<string>();
            context.Items[Key] = values;
        }

        values.Add(value);
    }

    public static string[] Read(HttpContext context) =>
        context.Items.TryGetValue(Key, out var existing) && existing is List<string> values
            ? values.ToArray()
            : Array.Empty<string>();
}