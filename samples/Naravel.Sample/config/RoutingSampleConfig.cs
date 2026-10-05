using Microsoft.Extensions.DependencyInjection;
using Naravel.Routing;
using Naravel.Sample.App.Http.Middleware;
using Naravel.Sample.App.Models;

namespace Naravel.Sample.Config;

public static class RoutingSampleConfig
{
    public static void Configure(IServiceCollection services)
    {
        services.AddTransient<StampMiddleware>();
        services.AddNaravelRouting(options =>
        {
            options.Pattern("id", "[0-9]+");
            options.Bind<RoutingProduct>("product", (value, _) => ValueTask.FromResult(RoutingProduct.Find(value)));

            options.Middleware
                .Alias<ApiKeyMiddleware>("api-key")
                .Alias<RoleMiddleware>("role")
                .Alias<AuditMiddleware>("audit")
                .Alias<TraceFirstMiddleware>("trace-first")
                .Alias<TraceSecondMiddleware>("trace-second")
                .Alias<StampMiddleware>("stamp")
                .Alias("inline", async (context, next, arguments) =>
                {
                    context.Response.Headers["X-Inline-Alias"] = arguments.At(0) ?? "active";
                    await next(context);
                })
                .Group("web", "audit")
                .Group("api", "trace-first", "trace-second", "api-key:X-Sample-Key,naravel-demo")
                .PrependToGroup("api", "audit")
                .AppendToGroup("api", "stamp")
                .ReplaceInGroup("api", "stamp", "inline:grouped")
                .RemoveFromGroup("api", "audit")
                .Priority(typeof(TraceSecondMiddleware), typeof(TraceFirstMiddleware));
        });
    }
}