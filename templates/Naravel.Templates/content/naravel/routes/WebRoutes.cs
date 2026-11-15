using Naravel.Routing;

namespace NaravelApp.Routes;

public static class WebRoutes
{
    public static void Map(WebApplication app)
    {
        // The health check: 200 while the application is up. A load balancer or a container probe can use it.
        app.MapHealthChecks("/up");

        app.MapNaravel(routes =>
        {
            routes.Get("/", (IWebHostEnvironment environment) =>
                    Results.File(Path.Combine(environment.ContentRootPath, "resources", "views", "welcome.html"), "text/html"))
                .Name("home");
        });
    }
}
