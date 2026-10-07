using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Naravel.Routing.Tests;

public class BindingsAndResourceTests
{
    private static Action<RoutingOptions> Users => o =>
        o.Bind<User>("user", (value, _) => ValueTask.FromResult<User?>(value == "1" ? new User(1, "Ann") : null));

    [Fact]
    public async Task Bindings_middleware_resolves_the_model_and_makes_unknown_values_404()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Get("/users/{user}", (HttpContext c) => c.GetRouteModel<User>("user")!.Name).Middleware("bindings"),
            options: Users);

        (await app.GetStringAsync("/users/1")).Should().Be("Ann");
        (await app.Client.GetAsync("/users/2")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bindings_can_be_applied_through_a_group()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Middleware("bindings").Group(g =>
                g.Get("/u/{user}", (HttpContext c) => c.GetRouteModel<User>("user")!.Id.ToString())),
            options: Users);

        (await app.GetStringAsync("/u/1")).Should().Be("1");
    }

    private static ResourceHandlers Handlers(string parameter = "photo") => new()
    {
        Index = () => "index",
        Create = () => "create",
        Store = () => "store",
        Show = (HttpContext c) => "show:" + c.Request.RouteValues[parameter],
        Edit = (HttpContext c) => "edit:" + c.Request.RouteValues[parameter],
        Update = (HttpContext c) => "update:" + c.Request.RouteValues[parameter],
        Destroy = (HttpContext c) => "destroy:" + c.Request.RouteValues[parameter],
    };

    [Fact]
    public async Task Resource_registers_the_seven_conventional_routes()
    {
        await using var app = await TestApp.StartAsync(r => r.Resource("photos", Handlers()));

        (await app.GetStringAsync("/photos")).Should().Be("index");
        (await app.GetStringAsync("/photos/create")).Should().Be("create");
        (await app.SendStringAsync(HttpMethod.Post, "/photos")).Should().Be("store");
        (await app.GetStringAsync("/photos/5")).Should().Be("show:5");
        (await app.GetStringAsync("/photos/5/edit")).Should().Be("edit:5");
        (await app.SendStringAsync(HttpMethod.Put, "/photos/5")).Should().Be("update:5");
        (await app.SendStringAsync(HttpMethod.Patch, "/photos/5")).Should().Be("update:5");
        (await app.SendStringAsync(HttpMethod.Delete, "/photos/5")).Should().Be("destroy:5");
    }

    [Fact]
    public async Task Resource_routes_are_named_after_the_resource()
    {
        await using var app = await TestApp.StartAsync(r => r.Resource("photos", Handlers()));

        app.Urls.Route("photos.index").Should().Be("/photos");
        app.Urls.Route("photos.show", new { photo = 5 }).Should().Be("/photos/5");
        app.Urls.Route("photos.edit", new { photo = 5 }).Should().Be("/photos/5/edit");
    }

    [Fact]
    public async Task ApiResource_skips_create_and_edit()
    {
        await using var app = await TestApp.StartAsync(r => r.ApiResource("photos", Handlers()));

        Action create = () => app.Urls.Route("photos.create");
        Action edit = () => app.Urls.Route("photos.edit", new { photo = 5 });

        create.Should().Throw<RouteNotFoundException>();
        edit.Should().Throw<RouteNotFoundException>();
        (await app.Client.GetAsync("/photos/5/edit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resource_Only_and_Except_filter_actions()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Resource("a", Handlers("a"), o => o.Only = new[] { "index", "show" });
            r.Resource("b", Handlers("b"), o => o.Except = new[] { "destroy" });
        });

        Action store = () => app.Urls.Route("a.store");
        Action destroy = () => app.Urls.Route("b.destroy", new { b = 1 });

        store.Should().Throw<RouteNotFoundException>();
        destroy.Should().Throw<RouteNotFoundException>();
        app.Urls.Route("a.show", new { a = 1 }).Should().Be("/a/1");
        app.Urls.Route("b.update", new { b = 1 }).Should().Be("/b/1");
    }

    [Fact]
    public async Task Resource_parameter_is_the_singular_of_the_last_segment_and_can_be_overridden()
    {
        await using var app = await TestApp.StartAsync(r =>
        {
            r.Resource("categories", Handlers("category"));
            r.Resource("things", Handlers("id"), o => o.Parameter = "id");
        });

        (await app.GetStringAsync("/categories/7")).Should().Be("show:7");
        (await app.GetStringAsync("/things/9")).Should().Be("show:9");
    }

    [Fact]
    public async Task Resource_inside_a_group_gets_prefix_name_prefix_and_group_middleware()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Prefix("api").Name("api.").Middleware("a").Group(g => g.ApiResource("photos", Handlers())));

        (await app.GetStringAsync("/api/photos/3")).Should().Be("show:3");
        app.Urls.Route("api.photos.index").Should().Be("/api/photos");
        app.Log.Events.Should().Equal(">A", "<A");
    }

    [Fact]
    public async Task Controller_resource_maps_seven_named_conventional_routes()
    {
        await using var app = await TestApp.StartAsync(
            native: endpoints => endpoints.MapNaravelControllerResource<PhotoController>("photos"),
            controllers: true);

        var routes = app.Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => (Endpoint: endpoint, Name: endpoint.Metadata.GetMetadata<RouteNameMetadata>()?.RouteName))
            .Where(item => item.Name?.StartsWith("photos.", StringComparison.Ordinal) == true)
            .ToArray();

        routes.Select(item => item.Name).Distinct().Should().BeEquivalentTo(
            "photos.index", "photos.create", "photos.store", "photos.show", "photos.edit", "photos.update", "photos.destroy");
        (await app.GetStringAsync("/photos")).Should().Be("index");
        (await app.GetStringAsync("/photos/create")).Should().Be("create");
        (await app.SendStringAsync(HttpMethod.Post, "/photos")).Should().Be("store");
        (await app.GetStringAsync("/photos/7")).Should().Be("show:7");
        (await app.GetStringAsync("/photos/7/edit")).Should().Be("edit:7");
        (await app.SendStringAsync(HttpMethod.Put, "/photos/7")).Should().Be("update:7");
        (await app.SendStringAsync(HttpMethod.Patch, "/photos/7")).Should().Be("update:7");
        (await app.SendStringAsync(HttpMethod.Delete, "/photos/7")).Should().Be("destroy:7");
    }
}

public sealed class PhotoController : ControllerBase
{
    public IActionResult Index() => Content("index");

    public IActionResult Create() => Content("create");

    public IActionResult Store() => Content("store");

    public IActionResult Show(string id) => Content("show:" + id);

    public IActionResult Edit(string id) => Content("edit:" + id);

    public IActionResult Update(string id) => Content("update:" + id);

    public IActionResult Destroy(string id) => Content("destroy:" + id);
}
