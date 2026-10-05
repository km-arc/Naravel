using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Naravel.Routing.Tests;

public class RoutingEndToEndTests
{
    [Fact]
    public async Task Get_answers_GET_and_HEAD()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/hello", () => "hi"));

        (await app.GetStringAsync("/hello")).Should().Be("hi");
        var head = await app.Client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/hello"));
        head.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Each_verb_helper_answers_its_own_method()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Post("/v", () => "post");
            r.Put("/v", () => "put");
            r.Patch("/v", () => "patch");
            r.Delete("/v", () => "delete");
            r.Options("/v", () => "options");
        });

        (await app.SendStringAsync(HttpMethod.Post, "/v")).Should().Be("post");
        (await app.SendStringAsync(HttpMethod.Put, "/v")).Should().Be("put");
        (await app.SendStringAsync(HttpMethod.Patch, "/v")).Should().Be("patch");
        (await app.SendStringAsync(HttpMethod.Delete, "/v")).Should().Be("delete");
        (await app.SendStringAsync(HttpMethod.Options, "/v")).Should().Be("options");
    }

    [Fact]
    public async Task Match_limits_methods_and_Any_accepts_all()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Match(new[] { "GET", "POST" }, "/m", () => "m");
            r.Any("/any", () => "any");
        });

        (await app.Client.PutAsync("/m", null)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await app.Client.PostAsync("/m", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.DeleteAsync("/any")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Groups_compose_prefixes_and_name_prefixes()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Prefix("admin").Name("admin.").Group(admin =>
            {
                admin.Get("users/{user}", (string user) => "u" + user).Name("users.show");
                admin.Prefix("v2").Name("v2.").Group(v2 => v2.Get("ping", () => "pong").Name("ping"));
            }));

        (await app.GetStringAsync("/admin/users/5")).Should().Be("u5");
        (await app.GetStringAsync("/admin/v2/ping")).Should().Be("pong");
        app.Urls.Route("admin.users.show", new { user = 5 }).Should().Be("/admin/users/5");
        app.Urls.Route("admin.v2.ping").Should().Be("/admin/v2/ping");
    }

    [Fact]
    public async Task Plain_Group_shares_the_current_settings()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Prefix("p").Group(p => p.Group(inner => inner.Get("x", () => "x"))));

        (await app.GetStringAsync("/p/x")).Should().Be("x");
    }

    [Fact]
    public async Task Url_generator_turns_extra_values_into_a_query_string()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/users/{user}", (string user) => user).Name("users.show"));

        app.Urls.Route("users.show", new { user = 5, page = 2 }).Should().Be("/users/5?page=2");
    }

    [Fact]
    public async Task Url_generator_throws_a_clear_exception_for_unknown_names_or_missing_values()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/users/{user}", (string user) => user).Name("users.show"));

        Action unknown = () => app.Urls.Route("nope");
        Action missing = () => app.Urls.Route("users.show");

        unknown.Should().Throw<RouteNotFoundException>().Which.RouteName.Should().Be("nope");
        missing.Should().Throw<RouteNotFoundException>();
    }

    [Fact]
    public async Task Url_generator_builds_absolute_urls_from_the_http_context()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/x", () => "x").Name("x"));
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("example.com");

        app.Urls.AbsoluteRoute(context, "x").Should().Be("https://example.com/x");
    }

    [Fact]
    public async Task Where_constrains_a_parameter_and_is_anchored()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/items/{id}", (string id) => "item" + id).Where("id", "[0-9]+"));

        (await app.Client.GetAsync("/items/abc")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.Client.GetAsync("/items/a1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.GetStringAsync("/items/12")).Should().Be("item12");
    }

    [Fact]
    public async Task Global_patterns_apply_to_every_route_with_that_parameter()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Get("/g/{id}", (string id) => id),
            options: o => o.Pattern("id", "[0-9]+"));

        (await app.Client.GetAsync("/g/abc")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.GetStringAsync("/g/7")).Should().Be("7");
    }

    [Fact]
    public async Task Where_for_a_missing_parameter_fails_at_startup()
    {
        Func<Task> act = () => TestApp.StartAsync(r => r.Get("/x", () => "x").Where("id", "[0-9]+"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Where(*");
    }

    [Fact]
    public async Task Domain_restricts_a_route_to_a_host()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/d", () => "d").Domain("api.example.com"));

        (await app.Client.GetAsync("http://api.example.com/d")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("http://other.example.com/d")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Group_domain_applies_to_its_routes()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Domain("api.example.com").Group(g => g.Get("/d", () => "d")));

        (await app.Client.GetAsync("http://api.example.com/d")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("http://other.example.com/d")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Fallback_handles_unmatched_requests_and_known_routes_still_win()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Get("/known", () => "known");
            r.Fallback((HttpContext c) =>
            {
                c.Response.StatusCode = 404;
                return c.Response.WriteAsync("custom-404");
            });
        });

        (await app.GetStringAsync("/known")).Should().Be("known");
        var missing = await app.Client.GetAsync("/nope/deep");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await missing.Content.ReadAsStringAsync()).Should().Be("custom-404");
    }

    [Fact]
    public async Task Redirect_is_302_by_default_and_301_when_permanent()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Redirect("/old", "/new");
            r.Redirect("/older", "/new", permanent: true);
        });

        var temporary = await app.Client.GetAsync("/old");
        var permanent = await app.Client.GetAsync("/older");

        temporary.StatusCode.Should().Be(HttpStatusCode.Redirect);
        temporary.Headers.Location!.ToString().Should().Be("/new");
        permanent.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
    }

    [Fact]
    public async Task Configure_gives_access_to_the_native_builder()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/c", () => "c").Configure(b => b.WithMetadata(new CustomMarker())));

        app.Endpoints.Should().Contain(e => e.Metadata.GetMetadata<CustomMarker>() != null);
    }

    [Fact]
    public async Task Unmatched_requests_pass_through_without_errors()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware("a"));

        (await app.Client.GetAsync("/missing")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Log.Events.Should().BeEmpty();
    }
}
