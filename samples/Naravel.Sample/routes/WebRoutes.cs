using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Naravel.Routing;
using Naravel.Sample.App.Http.Middleware;
using Naravel.Sample.App.Models;

namespace Naravel.Sample.Routes;

public static class WebRoutes
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/", () => Results.Redirect("/routing/"));
        app.MapGet("/health", () => Results.Ok(new { status = "ready" }));
        app.MapGet("/native/protected", () => Results.Ok(new { route = "native", access = "protected" }))
            .WithNaravelMiddleware("api-key");
        app.MapGet("/native/stamp", () => Results.Ok(new { route = "native", middleware = "IMiddleware" }))
            .WithNaravelMiddleware("stamp");

        app.MapGroup("/native/group")
            .WithNaravelMiddleware("api")
            .MapGet("/open", () => Results.Ok(new { route = "native group", access = "key excluded" }))
            .WithoutNaravelMiddleware("api-key");

        app.MapNaravel(routes => routes
            .Prefix("routing")
            .Name("routing.")
            .Middleware("web")
            .Group(routing =>
            {
                routing.Get("/", (IWebHostEnvironment environment) =>
                    Results.File(Path.Combine(environment.ContentRootPath, "resources", "views", "routing", "index.html"), "text/html"))
                    .Name("home");

                routing.Get("/urls", (HttpContext context, IUrlGenerator urls) => Results.Ok(new
                {
                    home = urls.Route("routing.home"),
                    book = urls.Route("routing.books.show", new { book = "7" }),
                    absoluteHome = urls.AbsoluteRoute(context, "routing.home")
                })).Name("urls");

                routing.Get("/users/{id}", (int id) => Results.Ok(new { id, constraint = "global and local regex" }))
                    .Where("id", "[0-9]+")
                    .Name("users.show");

                routing.Get("/catalog/{product}", (HttpContext context) =>
                    Results.Ok(context.GetRouteModel<RoutingProduct>("product")))
                    .Where("product", "[0-9]+")
                    .Middleware("bindings")
                    .Name("catalog.show");

                routing.Get("/verbs/get", () => Results.Ok(new { method = "GET (also HEAD)" }));
                routing.Post("/verbs/post", () => Results.Ok(new { method = "POST" }));
                routing.Put("/verbs/put", () => Results.Ok(new { method = "PUT" }));
                routing.Patch("/verbs/patch", () => Results.Ok(new { method = "PATCH" }));
                routing.Delete("/verbs/delete", () => Results.Ok(new { method = "DELETE" }));
                routing.Options("/verbs/options", () => Results.Ok(new { method = "OPTIONS" }));
                routing.Match(new[] { "GET", "POST" }, "/verbs/match", (HttpContext context) =>
                    Results.Ok(new { method = context.Request.Method }));
                routing.Any("/verbs/any", (HttpContext context) => Results.Ok(new { method = context.Request.Method }));
                routing.Redirect("/legacy", "/routing/");

                routing.Get("/middleware/priority", (HttpContext context) => Results.Ok(RouteTrace.Read(context)))
                    .Middleware("api");
                routing.Get("/middleware/role", () => Results.Ok(new { role = "admin accepted" }))
                    .Middleware("role:admin");
                routing.Get("/middleware/inline", () => Results.Ok(new { middleware = "inline alias" }))
                    .Middleware("inline:route-alias");
                routing.Get("/middleware/type", () => Results.Ok(new { middleware = "direct type" }))
                    .Middleware(typeof(ApiKeyMiddleware), "X-Sample-Key", "naravel-demo");
                routing.Get("/middleware/delegate", () => Results.Ok(new { middleware = "inline delegate" }))
                    .Middleware(async (context, next, arguments) =>
                    {
                        context.Response.Headers["X-Inline-Middleware"] = arguments.At(0) ?? "active";
                        await next(context);
                    });

                routing.Prefix("admin").Name("admin.").Middleware("api").Group(admin =>
                {
                    admin.Get("/dashboard", () => Results.Ok(new { area = "admin", access = "protected" }))
                        .Name("dashboard");
                    admin.Get("/public", () => Results.Ok(new { area = "admin", access = "middleware excluded" }))
                        .WithoutMiddleware("api-key");
                    admin.Get("/tenant", () => Results.Ok(new { tenant = "tenant.example.test" }))
                        .Domain("tenant.example.test");
                });

                routing.Resource("books", new ResourceHandlers
                {
                    Index = () => Results.Ok(new[] { "The Pragmatic Programmer", "Clean Code" }),
                    Create = () => Results.Ok(new { action = "create" }),
                    Store = () => Results.Ok(new { action = "store" }),
                    Show = (string book) => Results.Ok(new { action = "show", book }),
                    Edit = (string book) => Results.Ok(new { action = "edit", book }),
                    Update = (string book) => Results.Ok(new { action = "update", book }),
                    Destroy = (string book) => Results.Ok(new { action = "destroy", book })
                }, options => options.Parameter = "book");

                routing.Resource("reports", new ResourceHandlers
                {
                    Index = () => Results.Ok(new[] { "weekly", "monthly" }),
                    Show = (string report) => Results.Ok(new { report }),
                    Destroy = (string report) => Results.Ok(new { deleted = report })
                }, options =>
                {
                    options.Parameter = "report";
                    options.Only = new[] { "index", "show" };
                });

                routing.Prefix("api").Name("api.").Middleware("api").Group(api =>
                    api.ApiResource("widgets", new ResourceHandlers
                    {
                        Index = () => Results.Ok(new[] { "widget-a", "widget-b" }),
                        Store = () => Results.Ok(new { action = "store" }),
                        Show = (string widgetCode) => Results.Ok(new { action = "show", widgetCode }),
                        Update = (string widgetCode) => Results.Ok(new { action = "update", widgetCode }),
                        Destroy = (string widgetCode) => Results.Ok(new { action = "destroy", widgetCode })
                    }, options =>
                    {
                        options.Parameter = "widgetCode";
                        options.Except = new[] { "destroy" };
                    }));

                routing.Domain("tenant.example.test").Group(tenant =>
                    tenant.Get("/tenant/ping", () => Results.Ok(new { tenant = "tenant.example.test", status = "ready" }))
                        .Name("tenant.ping"));

                routing.Fallback(() => Results.NotFound(new { message = "No routing sample endpoint matched." }));
            }));
    }
}
