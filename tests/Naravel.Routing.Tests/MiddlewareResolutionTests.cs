using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Options;

namespace Naravel.Routing.Tests;

/// <summary>Pure unit tests of alias/group/exclusion/priority resolution (no HTTP host).</summary>
public class MiddlewareResolutionTests
{
    private static MiddlewarePipelineFactory Factory(Action<RoutingOptions>? configure = null)
    {
        var options = new RoutingOptions();
        options.Middleware.Alias<TraceA>("a").Alias<TraceB>("b").Alias<TraceC>("c").Alias<TraceD>("d");
        configure?.Invoke(options);
        return new MiddlewarePipelineFactory(Microsoft.Extensions.Options.Options.Create(options));
    }

    private static MiddlewareSpec[] Named(params string[] names) => names.Select(MiddlewareSpec.Named).ToArray();

    private static string[] Names(IEnumerable<ResolvedMiddleware> items) => items.Select(i => i.DisplayName).ToArray();

    private static Endpoint MakeEndpoint(params object[] metadata) =>
        new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse("/x"),
            0,
            new EndpointMetadataCollection(metadata),
            "x");

    [Fact]
    public void Alias_with_arguments_is_parsed_and_trimmed()
    {
        var result = Factory().Resolve(Named("a:60, 1"), Array.Empty<MiddlewareSpec>());

        result.Should().ContainSingle();
        result[0].Type.Should().Be(typeof(TraceA));
        result[0].DisplayName.Should().Be("a");
        result[0].Arguments.Should().Equal("60", "1");
    }

    [Fact]
    public void Unknown_alias_throws_with_a_helpful_message()
    {
        var factory = Factory();

        Action act = () => factory.Resolve(Named("nope"), Array.Empty<MiddlewareSpec>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*'nope' is not registered*");
    }

    [Fact]
    public void Group_expands_in_declared_order()
    {
        var factory = Factory(o => o.Middleware.Group("web", "a", "b"));

        Names(factory.Resolve(Named("web", "c"), Array.Empty<MiddlewareSpec>())).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void Groups_can_nest()
    {
        var factory = Factory(o => o.Middleware.Group("outer", "inner", "c").Group("inner", "a"));

        Names(factory.Resolve(Named("outer"), Array.Empty<MiddlewareSpec>())).Should().Equal("a", "c");
    }

    [Fact]
    public void Group_cycle_is_detected()
    {
        var factory = Factory(o => o.Middleware.Group("p", "q").Group("q", "p"));

        Action act = () => factory.Resolve(Named("p"), Array.Empty<MiddlewareSpec>());

        act.Should().Throw<InvalidOperationException>().WithMessage("*nested too deeply*");
    }

    [Fact]
    public void Group_wins_over_an_alias_with_the_same_name()
    {
        var factory = Factory(o => o.Middleware.Alias<TraceA>("web").Group("web", "b"));

        Names(factory.Resolve(Named("web"), Array.Empty<MiddlewareSpec>())).Should().Equal("b");
    }

    [Fact]
    public void Without_removes_by_type_regardless_of_arguments()
    {
        Names(Factory().Resolve(Named("a:1", "b"), Named("a"))).Should().Equal("b");
    }

    [Fact]
    public void Without_a_group_removes_all_of_its_members()
    {
        var factory = Factory(o => o.Middleware.Group("web", "a", "b"));

        Names(factory.Resolve(Named("web", "c"), Named("web"))).Should().Equal("c");
    }

    [Fact]
    public void Without_unknown_name_throws()
    {
        var factory = Factory();

        Action act = () => factory.Resolve(Named("a"), Named("typo"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Duplicates_with_the_same_arguments_keep_the_first_but_different_arguments_are_kept()
    {
        var result = Factory().Resolve(Named("a:1", "a:1", "a:2"), Array.Empty<MiddlewareSpec>());

        result.Should().HaveCount(2);
        result[0].Arguments.Should().Equal("1");
        result[1].Arguments.Should().Equal("2");
    }

    [Fact]
    public void A_type_reference_and_an_alias_to_the_same_type_are_duplicates()
    {
        var specs = new[] { MiddlewareSpec.OfType(typeof(TraceA)), MiddlewareSpec.Named("a") };

        Factory().Resolve(specs, Array.Empty<MiddlewareSpec>()).Should().ContainSingle();
    }

    [Fact]
    public void OfType_rejects_types_that_are_not_middleware()
    {
        Action act = () => MiddlewareSpec.OfType(typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Alias_rejects_types_that_are_not_middleware()
    {
        var options = new RoutingOptions();

        Action act = () => options.Middleware.Alias("x", typeof(string));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Priority_sorts_listed_middleware_inside_the_slots_they_occupy()
    {
        var factory = Factory(o => o.Middleware.Priority(typeof(TraceA), typeof(TraceB), typeof(TraceC)));

        // c, d, a, b: the listed ones (c, a, b) sit in slots 0, 2, 3 and are sorted there; d keeps slot 1.
        Names(factory.Resolve(Named("c", "d", "a", "b"), Array.Empty<MiddlewareSpec>())).Should().Equal("a", "d", "b", "c");
    }

    [Fact]
    public void Group_mutators_prepend_append_remove_and_replace()
    {
        var factory = Factory(o => o.Middleware
            .Group("g", "a", "c")
            .PrependToGroup("g", "d")
            .AppendToGroup("g", "b")
            .RemoveFromGroup("g", "c")
            .ReplaceInGroup("g", "a", "c"));

        Names(factory.Resolve(Named("g"), Array.Empty<MiddlewareSpec>())).Should().Equal("d", "c", "b");
    }

    [Fact]
    public void AppendToGroup_creates_a_missing_group_like_laravel()
    {
        var factory = Factory(o => o.Middleware.AppendToGroup("new", "a"));

        Names(factory.Resolve(Named("new"), Array.Empty<MiddlewareSpec>())).Should().Equal("a");
    }

    [Fact]
    public void The_bindings_alias_is_registered_by_default()
    {
        var factory = new MiddlewarePipelineFactory(Microsoft.Extensions.Options.Options.Create(new RoutingOptions()));

        factory.Resolve(Named("bindings"), Array.Empty<MiddlewareSpec>())
            .Should().ContainSingle().Which.Type.Should().Be(typeof(SubstituteBindings));
    }

    [Fact]
    public void Pipeline_is_built_once_per_endpoint()
    {
        var factory = Factory();
        var endpoint = MakeEndpoint(new RouteMiddlewareMetadata(Named("a"), Array.Empty<MiddlewareSpec>()));

        var first = factory.GetPipeline(endpoint);
        var second = factory.GetPipeline(endpoint);

        second.Should().BeSameAs(first);
        Names(first).Should().Equal("a");
    }

    [Fact]
    public void Controller_attributes_are_part_of_the_pipeline()
    {
        var factory = Factory();

        Names(factory.GetPipeline(MakeEndpoint(new MiddlewareAttribute("a")))).Should().Equal("a");
        factory.GetPipeline(MakeEndpoint(new MiddlewareAttribute("a"), new WithoutMiddlewareAttribute("a"))).Should().BeEmpty();
    }

    [Fact]
    public void Metadata_from_several_conventions_is_merged()
    {
        var factory = Factory();
        var endpoint = MakeEndpoint(
            new RouteMiddlewareMetadata(Named("a"), Array.Empty<MiddlewareSpec>()),
            new RouteMiddlewareMetadata(Named("b"), Named("a")));

        Names(factory.GetPipeline(endpoint)).Should().Equal("b");
    }

    [Fact]
    public void Arguments_expose_helpers()
    {
        var args = new MiddlewareArguments(new[] { "60", "x" });

        args.Count.Should().Be(2);
        args.At(0).Should().Be("60");
        args.At(5).Should().BeNull();
        args.Int(0, 1).Should().Be(60);
        args.Int(1, 7).Should().Be(7);
        MiddlewareArguments.Empty.Count.Should().Be(0);
    }

    [Fact]
    public void GetRouteModel_returns_null_without_a_binding()
    {
        new DefaultHttpContext().GetRouteModel<User>("user").Should().BeNull();
    }
}
