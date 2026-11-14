using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Naravel.Foundation.Tests;

// Runs the examples of docs/en/foundation.md ("Quickstart", "Drivers that read their settings", "Adding a driver while the
// application runs", "Choosing the registration overload", "Errors" and "Testing a module built on Foundation") and the
// "Quick start" of docs/nuget/Naravel.Foundation.md, which is the same Greeter module. The guide's quickstart is a web
// application; here its registrations and the calls of its endpoints run on a service provider, with no HTTP.
// ShoutingGreeter, RemoteGreeter and FakeGreeter are only named in the guide, so their bodies below are this test's own.
// If a test here needs a change, the guide and the package page need the same change.
public sealed class DocsExampleTests
{
    private static IConfigurationSection Section(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => "Greeter:" + s.Key, s => s.Value))
            .Build()
            .GetSection("Greeter");

    // The registrations of the quickstart, with the "Greeter" section made of the given settings.
    private static ServiceCollection Quickstart(params (string Key, string? Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(Section(settings));
        services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());
        return services;
    }

    [Fact]
    public void The_quickstart_answers_hello_with_the_driver_it_names()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();   // the parameter of the endpoint

        manager.Driver("polite").Greet("Ada").Should().Be("Hello, Ada!");   // GET /hello
    }

    [Fact]
    public void A_default_in_configuration_lets_Driver_be_called_without_a_name()
    {
        using var provider = Quickstart(("Default", "polite")).BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();

        manager.Driver().Greet("Ada").Should().Be("Hello, Ada!");   // GET /hi
        manager.Driver().Should().BeSameAs(manager.Driver("polite"));
        provider.GetRequiredService<IGreeter>().Should().BeSameAs(manager.Driver());   // the direct service is the default driver
    }

    [Fact]
    public void Without_a_default_Driver_with_no_name_gives_the_documented_message()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();

        var act = () => manager.Driver();

        act.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be(
            "No default driver is configured for IGreeter. Set the 'Default' option or pass a driver name explicitly.");
    }

    [Fact]
    public void A_driver_reads_its_settings_from_its_store()
    {
        var services = new ServiceCollection();
        services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>(
            Section(("Default", "friendly"), ("Stores:friendly:Greeting", "Hi")));
        services.AddNaravelDriver<IGreeter>("friendly", sp =>
        {
            var options = sp.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue;
            var store = options.GetStore("friendly");                 // the "Stores:friendly" section
            return new FriendlyGreeter(store["Greeting"] ?? "Hello");
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<GreeterManager>().Driver().Greet("Ada").Should().Be("Hi, Ada!");
    }

    [Fact]
    public void A_store_that_is_not_configured_gives_the_documented_message_and_lists_the_stores_that_exist()
    {
        using var none = Quickstart().BuildServiceProvider();
        using var two = Quickstart(("Stores:a:Greeting", "x"), ("Stores:b:Greeting", "y")).BuildServiceProvider();

        var withNone = () => none.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue.GetStore("friendly");
        var withTwo = () => two.GetRequiredService<IOptionsMonitor<GreeterOptions>>().CurrentValue.GetStore("friendly");

        withNone.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be(
            "Store 'friendly' is not configured. Configured stores: (none).");
        withTwo.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be(
            "Store 'friendly' is not configured. Configured stores: a, b.");
    }

    [Fact]
    public async Task Extend_adds_a_synchronous_and_an_asynchronous_driver_to_a_manager_that_is_already_built()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();

        manager.Extend("shout", _ => new ShoutingGreeter());                 // synchronous factory
        manager.Extend("remote", async (sp, ct) =>                           // asynchronous factory
        {
            var greeter = new RemoteGreeter();
            await greeter.ConnectAsync(ct);
            return greeter;
        });

        manager.Driver("shout").Greet("Ada").Should().Be("HELLO, ADA!");     // sync factories work with Driver()
        var early = () => manager.Driver("remote");
        early.Should().ThrowExactly<InvalidOperationException>().Which.Message.Should().Be(
            "Driver 'remote' is registered with an asynchronous factory and has not been created yet. Use DriverAsync to create it.");
        var remote = await manager.DriverAsync("remote", CancellationToken.None);   // async-only factories need DriverAsync()
        remote.Should().BeOfType<RemoteGreeter>().Which.Connected.Should().BeTrue();
        manager.Driver("remote").Should().BeSameAs(remote);                  // once created, Driver() returns it
    }

    [Fact]
    public void Registering_a_name_again_replaces_its_factory_and_the_old_instance_lives_until_the_manager_is_disposed()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();
        manager.Extend("shout", _ => new ShoutingGreeter());
        var old = (ShoutingGreeter)manager.Driver("shout");

        manager.Extend("shout", _ => new PoliteGreeter());

        manager.Driver("shout").Should().BeOfType<PoliteGreeter>();          // the next call builds one from the new factory
        old.Greet("Ada").Should().Be("HELLO, ADA!");                         // the holder of the old instance keeps using it
        old.DisposeCalls.Should().Be(0);
        provider.Dispose();
        old.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void The_overload_without_a_configuration_uses_the_options_the_caller_bound()
    {
        var services = new ServiceCollection();
        services.Configure<GreeterOptions>(Section(("Default", "polite")));
        services.AddNaravelManager<GreeterManager, IGreeter, GreeterOptions>();
        services.AddNaravelDriver<IGreeter>("polite", _ => new PoliteGreeter());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<GreeterManager>().Driver().Greet("Ada").Should().Be("Hello, Ada!");
    }

    [Fact]
    public void A_blank_name_and_a_name_that_is_not_registered_give_the_documented_errors()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();
        manager.Extend("shout", _ => new ShoutingGreeter());

        foreach (var blank in new[] { "", " " })
        {
            var act = () => manager.Driver(blank);

            var blankError = act.Should().ThrowExactly<ArgumentException>().Which;
            blankError.ParamName.Should().Be("name");
            blankError.Message.Should().StartWith("Driver name must not be blank.");
        }

        var unknown = () => manager.Driver("x");

        var error = unknown.Should().ThrowExactly<DriverNotRegisteredException>().Which;
        error.Message.Should().Be("Driver 'x' is not registered for IGreeter. Available drivers: polite, shout.");
        error.DriverName.Should().Be("x");
        error.DriverType.Should().Be("IGreeter");
        error.AvailableDrivers.Should().Equal("polite", "shout");
    }

    [Fact]
    public void A_manager_that_was_disposed_refuses_to_resolve_a_driver()
    {
        var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();

        provider.Dispose();

        var act = () => manager.Driver("polite");
        act.Should().ThrowExactly<ObjectDisposedException>();
    }

    [Fact]
    public void A_driver_that_fails_to_dispose_does_not_stop_the_others_and_its_failure_comes_back_in_an_AggregateException()
    {
        var provider = Quickstart().BuildServiceProvider();   // not disposed here: the test disposes the manager itself, and expects the failure
        var manager = provider.GetRequiredService<GreeterManager>();
        manager.Extend("bad", _ => new BrokenGreeter());
        manager.Extend("good", _ => new ShoutingGreeter());
        manager.Driver("bad");
        var good = (ShoutingGreeter)manager.Driver("good");

        var act = () => manager.Dispose();

        var error = act.Should().ThrowExactly<AggregateException>().Which;
        error.Message.Should().StartWith("The manager did not dispose cleanly: one or more drivers, lifetime callbacks or subscriptions failed.");
        error.InnerExceptions.Should().ContainSingle().Which.Message.Should().Be("dispose-failed");
        good.DisposeCalls.Should().Be(1);
    }

    // The test of the "Testing a module built on Foundation" section, as written there.
    [Fact]
    public void A_fake_registered_under_a_name_is_resolved_like_any_other_driver()
    {
        using var provider = Quickstart().BuildServiceProvider();
        var manager = provider.GetRequiredService<GreeterManager>();

        manager.Extend("fake", _ => new FakeGreeter());
        Assert.Equal("Hello, Ada!", manager.Driver("fake").Greet("Ada"));
    }
}

// The contract, the options and the manager of the guide's Greeter module. The manager is only a constructor.
internal interface IGreeter
{
    string Greet(string name);
}

public sealed class GreeterOptions : ManagerOptions
{
}

internal sealed class GreeterManager(IServiceProvider sp, IDriverRegistry<IGreeter> registry, IOptionsMonitor<GreeterOptions> options)
    : Manager<IGreeter, GreeterOptions>(sp, registry, options);

internal sealed class PoliteGreeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

internal sealed class FriendlyGreeter(string greeting) : IGreeter
{
    public string Greet(string name) => $"{greeting}, {name}!";
}

internal sealed class ShoutingGreeter : IGreeter, IDisposable
{
    public int DisposeCalls;

    public string Greet(string name) => $"HELLO, {name.ToUpperInvariant()}!";

    public void Dispose() => Interlocked.Increment(ref DisposeCalls);
}

internal sealed class RemoteGreeter : IGreeter
{
    public bool Connected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        Connected = true;
        return Task.CompletedTask;
    }

    public string Greet(string name) => $"Remote hello, {name}!";
}

internal sealed class FakeGreeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

internal sealed class BrokenGreeter : IGreeter, IDisposable
{
    public string Greet(string name) => name;

    public void Dispose() => throw new InvalidOperationException("dispose-failed");
}
