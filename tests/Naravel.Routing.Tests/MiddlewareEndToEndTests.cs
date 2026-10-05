using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Naravel.Routing.Tests;

public class MiddlewareEndToEndTests
{
    private static string Handler(Recorder log)
    {
        log.Add("h");
        return "ok";
    }

    [Fact]
    public async Task Group_and_route_middleware_run_in_order_with_arguments()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Middleware("a:x", "b").Group(g => g.Get("/m", (Recorder log) => Handler(log)).Middleware("c")));

        (await app.GetStringAsync("/m")).Should().Be("ok");

        app.Log.Events.Should().Equal(">A:x", ">B", ">C", "h", "<C", "<B", "<A:x");
    }

    [Fact]
    public async Task Configured_middleware_groups_expand()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Get("/m", (Recorder log) => Handler(log)).Middleware("web"),
            options: o => o.Middleware.Group("web", "a", "b"));

        await app.GetStringAsync("/m");

        app.Log.Events.Should().Equal(">A", ">B", "h", "<B", "<A");
    }

    [Fact]
    public async Task A_middleware_can_short_circuit_without_invoking_next()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/m", (Recorder log) => Handler(log)).Middleware("deny"));

        (await app.Client.GetAsync("/m")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        app.Log.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutMiddleware_on_a_route_removes_group_middleware()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Middleware("a", "b").Group(g => g.Get("/m", (Recorder log) => Handler(log)).WithoutMiddleware("a")));

        await app.GetStringAsync("/m");

        app.Log.Events.Should().Equal(">B", "h", "<B");
    }

    [Fact]
    public async Task WithoutMiddleware_by_type_removes_an_alias()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/m", (Recorder log) => Handler(log)).Middleware("a").WithoutMiddleware(typeof(TraceA)));

        await app.GetStringAsync("/m");

        app.Log.Events.Should().Equal("h");
    }

    [Fact]
    public async Task WithoutMiddleware_on_a_group_applies_to_all_its_routes()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Middleware("a", "b").Group(outer =>
                outer.WithoutMiddleware("b").Group(inner => inner.Get("/m", (Recorder log) => Handler(log)))));

        await app.GetStringAsync("/m");

        app.Log.Events.Should().Equal(">A", "h", "<A");
    }

    [Fact]
    public async Task Inline_route_middleware_runs()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/m", () => "ok").Middleware(async (context, next, args) =>
            {
                context.Response.Headers["X-Inline"] = "yes";
                await next(context);
            }));

        var response = await app.Client.GetAsync("/m");

        response.Headers.GetValues("X-Inline").Should().Equal("yes");
    }

    [Fact]
    public async Task Alias_can_point_to_an_inline_middleware_that_receives_arguments()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Get("/m", () => "ok").Middleware("echo:1,2"),
            options: o => o.Middleware.Alias("echo", async (context, next, args) =>
            {
                context.Response.Headers["X-Args"] = string.Join("|", args);
                await next(context);
            }));

        var response = await app.Client.GetAsync("/m");

        response.Headers.GetValues("X-Args").Should().Equal("1|2");
    }

    [Fact]
    public async Task Framework_IMiddleware_classes_can_be_used_through_the_adapter()
    {
        await using var app = await TestApp.StartAsync(r => r.Get("/m", () => "ok").Middleware("stamp"));

        var response = await app.Client.GetAsync("/m");

        response.Headers.GetValues("X-Stamp").Should().Equal("1");
    }

    [Fact]
    public async Task Terminable_middleware_runs_after_the_response()
    {
        await using var app = await TestApp.StartAsync(r =>
            r.Get("/m", (Recorder log) => Handler(log)).Middleware("t"));

        await app.GetStringAsync("/m");
        await Task.WhenAny(app.Log.Terminated.Task, Task.Delay(TimeSpan.FromSeconds(5)));

        app.Log.Terminated.Task.IsCompleted.Should().BeTrue();
        app.Log.Events.Should().Equal(">T", "h", "<T", "terminated");
    }

    [Fact]
    public async Task Priority_reorders_listed_middleware()
    {
        await using var app = await TestApp.StartAsync(
            r => r.Get("/m", (Recorder log) => Handler(log)).Middleware("a", "b"),
            options: o => o.Middleware.Priority(typeof(TraceB), typeof(TraceA)));

        await app.GetStringAsync("/m");

        app.Log.Events.Should().Equal(">B", ">A", "h", "<A", "<B");
    }

    [Fact]
    public async Task An_unknown_alias_fails_at_startup_not_on_the_first_request()
    {
        Func<Task> act = () => TestApp.StartAsync(r => r.Get("/x", () => "x").Middleware("nope"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nope*");
    }

    [Fact]
    public async Task Native_endpoints_can_use_Naravel_middleware()
    {
        await using var app = await TestApp.StartAsync(
            native: a => a.MapGet("/native", (Recorder log) => Handler(log)).WithNaravelMiddleware("a"));

        await app.GetStringAsync("/native");

        app.Log.Events.Should().Equal(">A", "h", "<A");
    }

    [Fact]
    public async Task Native_endpoint_can_exclude_middleware_of_its_native_group()
    {
        await using var app = await TestApp.StartAsync(native: a =>
        {
            var group = a.MapGroup("/g").WithNaravelMiddleware("a", "b");
            group.MapGet("/x", (Recorder log) => Handler(log)).WithoutNaravelMiddleware("a");
        });

        await app.GetStringAsync("/g/x");

        app.Log.Events.Should().Equal(">B", "h", "<B");
    }

    [Fact]
    public async Task Controller_attributes_apply_class_and_action_middleware()
    {
        await using var app = await TestApp.StartAsync(controllers: true);

        await app.GetStringAsync("/ctl/index");

        app.Log.Events.Should().Contain(new[] { ">A", ">B" }).And.NotContain(">C");
    }

    [Fact]
    public async Task Controller_Except_excludes_the_named_action()
    {
        await using var app = await TestApp.StartAsync(controllers: true);

        await app.GetStringAsync("/ctl/show");

        app.Log.Events.Should().Contain(">A").And.NotContain(">B").And.NotContain(">C");
    }

    [Fact]
    public async Task Controller_WithoutMiddleware_attribute_removes_class_middleware()
    {
        await using var app = await TestApp.StartAsync(controllers: true);

        (await app.GetStringAsync("/ctl/skip")).Should().Be("skip");

        app.Log.Events.Should().BeEmpty();
    }
}
