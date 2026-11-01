using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Foundation.Tests;

/// <summary>
/// A module can register its built-in drivers with access to the container, on the same registry as every other
/// registration, so AddNaravelDriver and ConfigureNaravelDrivers elsewhere are honoured and never silently ignored.
/// </summary>
public sealed class DriverRegistrationWithProviderTests
{
    private sealed record Marker(string Value);

    private static ServiceProvider Build(string @default, Action<IServiceCollection> configureServices)
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Fake:Default"] = @default })
            .Build();
        var services = new ServiceCollection();
        services.AddNaravelManager<FakeManager, IFakeDriver, ManagerOptions>(root.GetSection("Fake"));
        configureServices(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_action_receives_the_container_and_its_drivers_reach_the_manager()
    {
        using var provider = Build("late", services =>
        {
            services.AddSingleton(new Marker("from the container"));
            services.ConfigureNaravelDrivers<IFakeDriver>((sp, registry) =>
                registry.Register("late", _ => new FakeDriver(sp.GetRequiredService<Marker>().Value)));
        });

        provider.GetRequiredService<FakeManager>().Driver().Id.Should().Be("from the container");
        provider.GetRequiredService<IFakeDriver>().Id.Should().Be("from the container");
    }

    [Fact]
    public void The_action_runs_when_the_registry_is_created_not_when_it_is_added()
    {
        var calls = 0;
        using var provider = Build("late", services =>
            services.ConfigureNaravelDrivers<IFakeDriver>((serviceProvider, registry) =>
            {
                calls++;
                registry.Register("late", _ => new FakeDriver("late"));
            }));

        calls.Should().Be(0);
        provider.GetRequiredService<FakeManager>().Driver();
        provider.GetRequiredService<FakeManager>().Driver();

        calls.Should().Be(1);
    }

    [Fact]
    public void Drivers_from_every_kind_of_registration_share_one_registry_and_the_later_one_wins_a_name_clash()
    {
        using var provider = Build("shared", services =>
        {
            services.ConfigureNaravelDrivers<IFakeDriver>((serviceProvider, registry) =>
            {
                registry.Register("shared", _ => new FakeDriver("first"));
                registry.Register("only-in-provider-form", _ => new FakeDriver("provider form"));
            });
            services.ConfigureNaravelDrivers<IFakeDriver>(registry => registry.Register("only-in-plain-form", _ => new FakeDriver("plain form")));
            services.AddNaravelDriver<IFakeDriver>("shared", _ => new FakeDriver("second"));
        });

        var manager = provider.GetRequiredService<FakeManager>();

        manager.Driver().Id.Should().Be("second");
        manager.Driver("only-in-provider-form").Id.Should().Be("provider form");
        manager.Driver("only-in-plain-form").Id.Should().Be("plain form");
    }

    [Fact]
    public void A_driver_registered_this_way_is_owned_by_the_container_once_when_it_is_injected_directly()
    {
        var driver = new FakeDriver("owned");
        var provider = Build("owned", services =>
            services.ConfigureNaravelDrivers<IFakeDriver>((serviceProvider, registry) => registry.Register("owned", _ => driver)));

        provider.GetRequiredService<IFakeDriver>().Should().BeSameAs(driver);
        provider.Dispose();

        driver.DisposeCalls.Should().Be(1);
    }
}
