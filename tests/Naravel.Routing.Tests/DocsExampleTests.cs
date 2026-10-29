using System.Net;
using Microsoft.AspNetCore.Http;

namespace Naravel.Routing.Tests;

public sealed record Book(int Id, string Title);

/// <summary>A route middleware with an argument ("role:admin"), as in the guide's complete example.</summary>
public sealed class RequireRole : IRouteMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments)
    {
        var role = arguments.At(0) ?? "user";
        if (context.Request.Headers["X-Role"] != role)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context);
    }
}

// Runs "Complete example" of docs/en/routing.md and docs/fa/routing.md. IBookStore is replaced by a lookup
// table. If a test here needs a change, the guide needs the same change.
public class DocsExampleTests
{
    private static Task<TestApp> StartAsync() => TestApp.StartAsync(
        routes: r =>
        {
            r.Get("/", () => "home").Name("home");
            r.Post("/login", (HttpContext c) => Results.Ok()).Middleware("throttle:login");

            r.Prefix("api").Name("api.").Middleware("api").Group(api =>
            {
                api.Get("books/{book}", (HttpContext c) => c.GetRouteModel<Book>("book")).Name("books.show");

                api.ApiResource("authors", new ResourceHandlers
                {
                    Index = () => Results.Ok(new[] { "Ada", "Alan" }),
                    Show = (string author) => Results.Ok(author),
                    Destroy = (string author) => Results.NoContent(),
                });

                api.Prefix("admin").Name("admin.").Middleware("role:admin").Group(admin =>
                    admin.Get("stats", () => Results.Ok("stats")).Name("stats"));
            });

            r.Get("/links", (IUrlGenerator urls) => Results.Ok(new
            {
                book = urls.Route("api.books.show", new { book = 7 }),
                stats = urls.Route("api.admin.stats"),
            })).Name("links");

            r.Fallback(() => Results.NotFound());
        },
        options: o =>
        {
            o.Middleware.Alias<RequireRole>("role");
            o.Middleware.Group("api", "bindings", "throttle:60,1");
            o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1));
            o.Pattern("id", "[0-9]+");
            o.Bind<Book>("book", (value, _) => ValueTask.FromResult<Book?>(value == "7" ? new Book(7, "Dune") : null));
        });

    [Fact]
    public async Task Group_binding_resource_and_named_urls_work_as_the_guide_says()
    {
        await using var app = await StartAsync();

        (await app.GetStringAsync("/")).Should().Be("home");
        (await app.GetStringAsync("/api/books/7")).Should().Contain("Dune");
        (await app.Client.GetAsync("/api/books/9")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.GetStringAsync("/api/authors")).Should().Contain("Ada");
        (await app.GetStringAsync("/api/authors/ada")).Should().Contain("ada");
        (await app.Client.DeleteAsync("/api/authors/ada")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await app.Client.GetAsync("/nope")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var links = await app.GetStringAsync("/links");
        links.Should().Contain("/api/books/7").And.Contain("/api/admin/stats");
    }

    [Fact]
    public async Task Role_middleware_takes_its_argument_and_short_circuits()
    {
        await using var app = await StartAsync();

        (await app.Client.GetAsync("/api/admin/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/stats");
        request.Headers.Add("X-Role", "admin");
        var response = await app.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("stats");
    }

    [Fact]
    public async Task Throttle_policy_admits_five_then_answers_429_with_Retry_After()
    {
        await using var app = await StartAsync();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 7; i++)
        {
            last = await app.Client.PostAsync("/login", null);
            statuses.Add(last.StatusCode);
        }

        statuses.Take(5).Should().OnlyContain(s => s == HttpStatusCode.OK);
        statuses.Skip(5).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        last!.Headers.Contains("Retry-After").Should().BeTrue();
    }
}
