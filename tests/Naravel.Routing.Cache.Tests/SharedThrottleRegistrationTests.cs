using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Testing;
using Naravel.Foundation;

namespace Naravel.Routing.Cache.Tests;

/// <summary>How <c>AddNaravelSharedThrottle</c> is wired, and the direction of the dependencies (PDR-024, hard rule 3).</summary>
public sealed class SharedThrottleRegistrationTests
{
    private static ServiceCollection WithCache(params (string Name, ICacheStore Store)[] stores)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(new ConfigurationBuilder().Build());
        foreach (var (name, store) in stores) services.AddNaravelDriver<ICacheStore>(name, _ => store);
        return services;
    }

    [Fact]
    public void Without_the_package_the_throttle_counts_in_process()
    {
        var services = WithCache();
        services.AddNaravelRouting();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThrottleStore>().GetType().Name.Should().Be("InProcessThrottleStore");
    }

    [Fact]
    public void The_shared_store_replaces_the_default_whichever_registration_comes_first()
    {
        var routingFirst = WithCache(("shared", new CacheFake()));
        routingFirst.AddNaravelRouting();
        routingFirst.AddNaravelSharedThrottle("shared");

        var sharedFirst = WithCache(("shared", new CacheFake()));
        sharedFirst.AddNaravelSharedThrottle("shared");
        sharedFirst.AddNaravelRouting();

        using var a = routingFirst.BuildServiceProvider();
        using var b = sharedFirst.BuildServiceProvider();
        a.GetRequiredService<IThrottleStore>().Should().BeOfType<CacheThrottleStore>();
        b.GetRequiredService<IThrottleStore>().Should().BeOfType<CacheThrottleStore>();
        routingFirst.Count(descriptor => descriptor.ServiceType == typeof(IThrottleStore)).Should().Be(1);
        sharedFirst.Count(descriptor => descriptor.ServiceType == typeof(IThrottleStore)).Should().Be(1);
    }

    [Fact]
    public async Task The_last_registration_wins()
    {
        var first = new CacheFake("first");
        var second = new CacheFake("second");
        var services = WithCache(("first", first), ("second", second));
        services.AddNaravelSharedThrottle("first");
        services.AddNaravelSharedThrottle("second");
        using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IThrottleStore>().AttemptAsync("p", 1, TimeSpan.FromMinutes(1), CancellationToken.None);

        second.Keys.Should().HaveCount(1);
        first.Keys.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_store_name_fails_at_the_first_use_and_names_the_stores_that_exist()
    {
        var services = WithCache(("shared", new CacheFake()));
        services.AddNaravelSharedThrottle("nope");
        using var provider = services.BuildServiceProvider();

        var failure = Record.Exception(() => provider.GetRequiredService<IThrottleStore>());

        failure.Should().BeOfType<DriverNotRegisteredException>();
        failure!.Message.Should().Contain("nope");
        failure.Message.Should().Contain("shared");
    }

    [Fact]
    public void Without_Naravel_Cache_the_failure_names_what_is_missing()
    {
        var services = new ServiceCollection();
        services.AddNaravelSharedThrottle("shared");
        using var provider = services.BuildServiceProvider();

        var failure = Record.Exception(() => provider.GetRequiredService<IThrottleStore>());

        failure.Should().BeOfType<InvalidOperationException>();
        failure!.Message.Should().Contain("CacheManager");
    }

    [Fact]
    public void A_blank_name_or_a_missing_collection_is_refused()
    {
        Action blank = () => new ServiceCollection().AddNaravelSharedThrottle(" ");
        Action none = () => new ServiceCollection().AddNaravelSharedThrottle(string.Empty);
        Action noServices = () => NaravelRoutingCacheServiceCollectionExtensions.AddNaravelSharedThrottle(null!, "shared");

        blank.Should().Throw<ArgumentException>();
        none.Should().Throw<ArgumentException>();
        noServices.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Routing_does_not_reference_Cache_and_the_adapter_references_only_Routing_Cache_and_Foundation()
    {
        var routing = typeof(RoutingOptions).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        routing.Should().NotContain("Naravel.Cache");
        routing.Should().NotContain("Naravel.Foundation");

        var adapter = typeof(CacheThrottleStore).Assembly.GetReferencedAssemblies().Select(name => name.Name).ToArray();
        adapter.Where(name => name!.StartsWith("Naravel.", StringComparison.Ordinal)).OrderBy(name => name)
            .Should().Equal("Naravel.Cache", "Naravel.Foundation", "Naravel.Routing");
        adapter.Should().NotContain("StackExchange.Redis");
    }
}
