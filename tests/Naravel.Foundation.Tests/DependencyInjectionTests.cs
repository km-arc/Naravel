using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Naravel.Foundation.Tests;

public class DependencyInjectionTests
{
    private static (ServiceProvider Provider, IConfigurationRoot Root) Build(
        Dictionary<string, string?> data,
        Action<IServiceCollection>? configureServices = null)
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        var services = new ServiceCollection();
        services.AddNaravelManager<FakeManager, IFakeDriver, ManagerOptions>(root.GetSection("Fake"));
        configureServices?.Invoke(services);
        return (services.BuildServiceProvider(), root);
    }

    private static Dictionary<string, string?> Config(string @default = "redis", string host = "h1") => new()
    {
        ["Fake:Default"] = @default,
        ["Fake:Stores:Redis:Host"] = host,
        ["Fake:Stores:File:Path"] = "cache",
        ["Other:Setting"] = "1",
    };

    private static IFakeDriver HostDriver(IServiceProvider sp) =>
        new FakeDriver(sp.GetRequiredService<IOptionsMonitor<ManagerOptions>>().CurrentValue.GetStore("redis")["Host"]!);

    [Fact]
    public void Options_bind_default_and_case_insensitive_stores()
    {
        var (provider, _) = Build(Config());

        var options = provider.GetRequiredService<IOptionsMonitor<ManagerOptions>>().CurrentValue;

        options.Default.Should().Be("redis");
        options.Stores.Keys.Should().BeEquivalentTo(new[] { "Redis", "File" });
        options.GetStore("REDIS")["Host"].Should().Be("h1");
    }

    [Fact]
    public void Missing_store_gives_a_helpful_error()
    {
        var (provider, _) = Build(Config());
        var options = provider.GetRequiredService<IOptionsMonitor<ManagerOptions>>().CurrentValue;
        Action act = () => options.GetStore("nope");

        act.Should().Throw<InvalidOperationException>().WithMessage("*nope*File, Redis*");
    }

    [Fact]
    public void Manager_registry_and_default_driver_are_registered()
    {
        var (provider, _) = Build(Config(), services => services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));

        var manager = provider.GetRequiredService<FakeManager>();

        provider.GetRequiredService<IDriverRegistry<IFakeDriver>>().IsRegistered("redis").Should().BeTrue();
        provider.GetRequiredService<FakeManager>().Should().BeSameAs(manager);
        provider.GetRequiredService<IFakeDriver>().Should().BeSameAs(manager.Driver());
    }

    [Fact]
    public void Built_in_drivers_read_their_own_store_configuration()
    {
        var (provider, _) = Build(Config(host: "redis.example"), services =>
            services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));

        provider.GetRequiredService<FakeManager>().Driver("redis").Id.Should().Be("redis.example");
    }

    [Fact]
    public void ConfigureNaravelDrivers_registers_several_drivers()
    {
        var (provider, _) = Build(Config(), services => services.ConfigureNaravelDrivers<IFakeDriver>(registry =>
        {
            registry.Register("redis", _ => new FakeDriver("r"));
            registry.Register("file", _ => new FakeDriver("f"));
        }));

        provider.GetRequiredService<IDriverRegistry<IFakeDriver>>().Names.Should().BeEquivalentTo(new[] { "file", "redis" });
    }

    [Fact]
    public async Task Async_built_in_drivers_work_through_DriverAsync()
    {
        var (provider, _) = Build(Config(), services => services.AddNaravelDriver<IFakeDriver>("redis", async (sp, ct) =>
        {
            await Task.Yield();
            return new FakeDriver("async-redis");
        }));

        var driver = await provider.GetRequiredService<FakeManager>().DriverAsync("redis");

        driver.Id.Should().Be("async-redis");
    }

    [Fact]
    public void Drivers_can_be_added_at_runtime_after_the_provider_is_built()
    {
        var (provider, _) = Build(Config(), services => services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));
        var manager = provider.GetRequiredService<FakeManager>();

        manager.Extend("custom", _ => new FakeDriver("custom"));

        manager.Driver("custom").Id.Should().Be("custom");
    }

    [Fact]
    public void A_real_configuration_reload_rebuilds_the_driver_with_the_new_values()
    {
        var (provider, root) = Build(Config(host: "h1"), services => services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));
        var manager = provider.GetRequiredService<FakeManager>();
        var first = manager.Driver();
        first.Id.Should().Be("h1");

        root["Fake:Stores:Redis:Host"] = "h2";
        root.Reload();

        var second = manager.Driver();
        second.Id.Should().Be("h2");
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public void An_unrelated_configuration_reload_does_not_rebuild_drivers()
    {
        var (provider, root) = Build(Config(), services => services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));
        var manager = provider.GetRequiredService<FakeManager>();
        var first = manager.Driver();

        root["Other:Setting"] = "2";
        root.Reload();

        manager.Driver().Should().BeSameAs(first);
    }

    [Fact]
    public void A_changed_default_in_configuration_takes_effect_without_rebuilding()
    {
        var (provider, root) = Build(Config(), services => services.ConfigureNaravelDrivers<IFakeDriver>(registry =>
        {
            registry.Register("redis", _ => new FakeDriver("r"));
            registry.Register("file", _ => new FakeDriver("f"));
        }));
        var manager = provider.GetRequiredService<FakeManager>();
        var redis = manager.Driver();

        root["Fake:Default"] = "file";
        root.Reload();

        manager.Driver().Id.Should().Be("f");
        manager.Driver("redis").Should().BeSameAs(redis);
    }

    [Fact]
    public void Disposing_the_provider_disposes_drivers()
    {
        var (provider, _) = Build(Config(), services => services.AddNaravelDriver<IFakeDriver>("redis", HostDriver));
        var driver = (FakeDriver)provider.GetRequiredService<FakeManager>().Driver();

        provider.Dispose();

        driver.IsDisposed.Should().BeTrue();
    }
}
