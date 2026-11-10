using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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

// Runs "Quickstart", "Complete example", "Errors" and "Testing" of docs/en/routing.md. IBookStore is replaced by a lookup
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

// The Quickstart of docs/en/routing.md, run on TestServer instead of Kestrel.
public class DocsQuickstartTests
{
    [Fact]
    public async Task The_quickstart_routes_answer_as_the_guide_says()
    {
        await using var app = await TestApp.StartAsync(
            routes: r =>
            {
                r.Get("/", () => "home").Name("home");

                r.Prefix("api").Name("api.").Middleware("throttle:api").Group(api =>
                {
                    api.Get("users/{id}", (int id) => $"user {id}").Where("id", "[0-9]+").Name("users.show");
                });

                r.Get("/links", (IUrlGenerator urls) => urls.Route("api.users.show", new { id = 7 }));
            },
            options: o => o.ConfigureThrottlePolicy("api", 60, TimeSpan.FromMinutes(1)));

        (await app.GetStringAsync("/")).Should().Be("home");

        var user = await app.Client.GetAsync("/api/users/7");
        (await user.Content.ReadAsStringAsync()).Should().Be("user 7");
        user.Headers.GetValues("X-RateLimit-Limit").Should().Equal("60");
        user.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("59");

        (await app.Client.GetAsync("/api/users/abc")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.GetStringAsync("/links")).Should().Be("/api/users/7");
    }
}

// The snippets of "Testing" in docs/en/routing.md. The host test is the guide's code, with the test class renamed.
public class DocsTestingTests
{
    [Fact]
    public async Task A_route_middleware_can_be_run_without_a_host()
    {
        var context = new DefaultHttpContext();
        var reached = false;

        await new RequireRole().InvokeAsync(
            context,
            _ => { reached = true; return Task.CompletedTask; },
            new MiddlewareArguments(new[] { "admin" }));

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);

        context.Request.Headers["X-Role"] = "admin";
        await new RequireRole().InvokeAsync(
            context,
            _ => { reached = true; return Task.CompletedTask; },
            new MiddlewareArguments(new[] { "admin" }));

        Assert.True(reached);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNaravelRouting(o => o.ConfigureThrottlePolicy("api", 2, TimeSpan.FromMinutes(1)));

        var app = builder.Build();
        app.UseNaravelRouting();
        app.MapNaravel(r =>
            r.Prefix("api").Name("api.").Middleware("throttle:api").Group(api =>
                api.Get("users/{id}", (int id) => $"user {id}").Where("id", "[0-9]+").Name("users.show")));

        await app.StartAsync();
        return app;
    }

    [Fact]
    public async Task The_third_request_in_a_minute_is_throttled()
    {
        await using var app = await StartAsync();
        using var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/7")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/7")).StatusCode);

        var third = await client.GetAsync("/api/users/7");
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_temporary_link_is_accepted_before_its_time_and_refused_after()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/download", () => "file").Name("download").Middleware("signed"));

        var live = app.Urls.TemporarySignedRoute("download", DateTimeOffset.UtcNow.AddMinutes(5));
        var expired = app.Urls.TemporarySignedRoute("download", DateTimeOffset.UtcNow.AddMinutes(-1));

        (await app.Client.GetAsync(live)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync(expired)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

// The messages of "Errors" in docs/en/routing.md. Each assertion is the full text from the source.
public class DocsErrorsTests
{
    private sealed class Photos : ControllerBase;

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception =>
        (await act.Should().ThrowAsync<T>()).Which;

    [Fact]
    public async Task An_unknown_alias_on_a_Naravel_route_fails_at_startup_with_the_message_in_the_guide()
    {
        var error = await Thrown<InvalidOperationException>(
            () => TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware("nope")));

        error.Message.Should().Be(
            "Middleware 'nope' is not registered. Register it with options.Middleware.Alias(...) or options.Middleware.Group(...).");
    }

    [Fact]
    public async Task Where_for_a_parameter_the_uri_does_not_have_fails_with_the_message_in_the_guide()
    {
        var error = await Thrown<InvalidOperationException>(
            () => TestApp.StartAsync(r => r.Get("/x", () => "x").Where("id", "[0-9]+")));

        error.Message.Should().Be("Route '/x' has Where(\"id\", ...) but no {id} parameter.");

        var inGroup = await Thrown<InvalidOperationException>(
            () => TestApp.StartAsync(r => r.Prefix("api").Group(api => api.Get("x", () => "x").Where("id", "[0-9]+"))));

        inGroup.Message.Should().Be("Route '/api/x' has Where(\"id\", ...) but no {id} parameter.");
    }

    [Fact]
    public async Task Groups_that_contain_each_other_fail_with_the_message_in_the_guide()
    {
        var error = await Thrown<InvalidOperationException>(() => TestApp.StartAsync(
            r => r.Get("/x", () => "x").Middleware("a-group"),
            options: o =>
            {
                o.Middleware.Group("a-group", "b-group");
                o.Middleware.Group("b-group", "a-group");
            }));

        error.Message.Should().Be("Middleware groups are nested too deeply (is there a cycle?).");
    }

    private abstract class AbstractMiddleware : IRouteMiddleware
    {
        public abstract Task InvokeAsync(HttpContext context, RequestDelegate next, MiddlewareArguments arguments);
    }

    [Fact]
    public async Task A_type_that_is_not_a_route_middleware_is_refused_with_the_message_in_the_guide()
    {
        Action abstractType = () => new RoutingOptions().Middleware.Alias("x", typeof(AbstractMiddleware));
        abstractType.Should().Throw<ArgumentException>().Which.Message.Should().StartWith($"'{typeof(AbstractMiddleware).FullName}' must be a concrete class");

        Action alias = () => new RoutingOptions().Middleware.Alias("x", typeof(string));
        Func<Task> route = () => TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware(typeof(string)));

        var errors = new[]
        {
            alias.Should().Throw<ArgumentException>().Which,
            (await route.Should().ThrowAsync<ArgumentException>()).Which,
        };

        foreach (var error in errors)
        {
            error.ParamName.Should().Be("type");
            error.Message.Should().StartWith(
                "'System.String' must be a concrete class implementing IRouteMiddleware or Microsoft.AspNetCore.Http.IMiddleware.");
        }
    }

    [Fact]
    public void A_throttle_policy_with_a_blank_name_a_zero_limit_or_a_zero_window_is_refused()
    {
        var options = new RoutingOptions();

        Action blank = () => options.ConfigureThrottlePolicy(" ", 5, TimeSpan.FromMinutes(1));
        Action noLimit = () => options.ConfigureThrottlePolicy("login", 0, TimeSpan.FromMinutes(1));
        Action noWindow = () => options.ConfigureThrottlePolicy("login", 5, TimeSpan.Zero);

        blank.Should().Throw<ArgumentException>();
        noLimit.Should().Throw<ArgumentOutOfRangeException>();
        noWindow.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task A_missing_AddNaravelRouting_fails_in_MapNaravel_with_the_container_error()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        Action act = () => app.MapNaravel(r => r.Get("/x", () => "x"));

        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("MiddlewarePipelineFactory");
    }

    [Fact]
    public async Task MapNaravelControllerResource_refuses_a_bad_name_or_type_with_the_messages_in_the_guide()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        Action badName = () => app.MapNaravelControllerResource<SampleController>("photos/{id}");
        Action notController = () => app.MapNaravelControllerResource<string>("photos");
        Action noSuffix = () => app.MapNaravelControllerResource<Photos>("photos");
        Action slash = () => app.MapNaravelControllerResource<SampleController>("/");
        Action blank = () => app.MapNaravelControllerResource<SampleController>(" ");

        foreach (var act in new[] { badName, slash })
        {
            var name = act.Should().Throw<ArgumentException>().Which;
            name.ParamName.Should().Be("name");
            name.Message.Should().StartWith("The resource name must be a non-empty route path without route parameters.");
        }

        var empty = blank.Should().Throw<ArgumentException>().Which;
        empty.ParamName.Should().Be("name");
        empty.Message.Should().NotContain("route path");

        var type = notController.Should().Throw<ArgumentException>().Which;
        type.ParamName.Should().Be("TController");
        type.Message.Should().StartWith("The resource type must derive from ControllerBase.");

        var suffix = noSuffix.Should().Throw<ArgumentException>().Which;
        suffix.ParamName.Should().Be("TController");
        suffix.Message.Should().StartWith("The resource type must use the MVC Controller suffix.");
    }

    [Fact]
    public async Task An_unknown_route_name_or_a_missing_value_gives_the_message_in_the_guide()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/users/{user}", (string user) => user).Name("users.show"));

        Action unknown = () => app.Urls.Route("nope");
        Action missing = () => app.Urls.Route("users.show");

        var first = unknown.Should().Throw<RouteNotFoundException>().Which;
        first.RouteName.Should().Be("nope");
        first.Message.Should().Be(
            "No route named 'nope' could be generated. Check the name and that all required parameters were supplied.");
        missing.Should().Throw<RouteNotFoundException>().Which.Message.Should().Be(
            "No route named 'users.show' could be generated. Check the name and that all required parameters were supplied.");
    }

    [Fact]
    public async Task A_signed_route_value_called_signature_or_expires_is_refused_with_the_message_in_the_guide()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/download", () => "file").Name("download").Middleware("signed"));

        Action signature = () => app.Urls.SignedRoute("download", new { signature = "x" });
        Action expires = () => app.Urls.TemporarySignedRoute("download", DateTimeOffset.UtcNow.AddMinutes(1), new { expires = "1" });

        foreach (var act in new[] { signature, expires })
        {
            act.Should().Throw<ArgumentException>().Which.Message.Should().StartWith(
                "Signed route values cannot contain reserved 'signature' or 'expires' query keys.");
        }
    }

    [Fact]
    public async Task An_unknown_alias_on_a_native_endpoint_fails_on_its_first_request_not_at_startup()
    {
        await using var app = await TestApp.StartAsync(
            native: a => a.MapGet("/native", () => "native").WithNaravelMiddleware("nope"));

        var error = await Thrown<InvalidOperationException>(() => app.Client.GetAsync("/native"));

        error.Message.Should().Be(
            "Middleware 'nope' is not registered. Register it with options.Middleware.Alias(...) or options.Middleware.Group(...).");
    }

    [Theory]
    [InlineData("throttle:nope", "Throttle middleware expects a configured policy name or positive permit-limit and window-minute arguments.")]
    [InlineData("throttle:0,1", "Throttle middleware expects a configured policy name or positive permit-limit and window-minute arguments.")]
    [InlineData("throttle", "Throttle middleware expects a configured policy name or positive permit-limit and window-minute arguments.")]
    [InlineData("cache.headers", "cache.headers requires one or more Cache-Control directives.")]
    public async Task A_middleware_with_arguments_it_cannot_use_fails_on_the_request_with_the_message_in_the_guide(string reference, string message)
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware(reference));

        var error = await Thrown<InvalidOperationException>(() => app.Client.GetAsync("/x"));

        error.Message.Should().Be(message);
    }

    [Fact]
    public async Task Without_UseNaravelRouting_the_route_middleware_does_not_run_and_nothing_is_reported()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddNaravelRouting(o => o.Middleware.Alias<Deny>("deny"));
        await using var app = builder.Build();
        app.MapNaravel(r => r.Get("/x", () => "x").Middleware("deny"));
        await app.StartAsync();
        using var client = ((TestServer)app.Services.GetRequiredService<IServer>()).CreateClient();

        var response = await client.GetAsync("/x");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("x");

        // The same route with UseNaravelRouting: Deny short-circuits.
        await using var withRunner = await TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware("deny"));
        (await withRunner.Client.GetAsync("/x")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
