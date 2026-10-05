namespace Naravel.Foundation.Tests;

public class DriverRegistryTests
{
    [Fact]
    public void Register_makes_name_resolvable_case_insensitively()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("Redis", _ => new FakeDriver("r"));

        registry.IsRegistered("redis").Should().BeTrue();
        registry.IsRegistered("REDIS").Should().BeTrue();
        registry.IsRegistered("file").Should().BeFalse();
    }

    [Fact]
    public void Create_invokes_sync_factory_with_the_given_provider()
    {
        IServiceProvider? received = null;
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", sp =>
        {
            received = sp;
            return new FakeDriver("a");
        });

        var driver = registry.Create("a", TestKit.EmptyProvider);

        driver.Id.Should().Be("a");
        received.Should().BeSameAs(TestKit.EmptyProvider);
    }

    [Fact]
    public async Task CreateAsync_uses_an_async_factory()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", async (sp, ct) =>
        {
            await Task.Yield();
            return new FakeDriver("async");
        });

        var driver = await registry.CreateAsync("a", TestKit.EmptyProvider);

        driver.Id.Should().Be("async");
    }

    [Fact]
    public async Task CreateAsync_also_works_for_sync_factories()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", _ => new FakeDriver("sync"));

        var driver = await registry.CreateAsync("a", TestKit.EmptyProvider);

        driver.Id.Should().Be("sync");
    }

    [Fact]
    public void Create_on_an_async_only_registration_throws_a_clear_error()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", (sp, ct) => new ValueTask<IFakeDriver>(new FakeDriver("a")));

        registry.IsAsyncOnly("a").Should().BeTrue();
        Action act = () => registry.Create("a", TestKit.EmptyProvider);

        act.Should().Throw<InvalidOperationException>().WithMessage("*asynchronous factory*CreateAsync*");
    }

    [Fact]
    public void IsAsyncOnly_is_false_for_sync_and_unknown_names()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", _ => new FakeDriver("a"));

        registry.IsAsyncOnly("a").Should().BeFalse();
        registry.IsAsyncOnly("missing").Should().BeFalse();
    }

    [Fact]
    public void Registering_the_same_name_replaces_the_factory()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("a", _ => new FakeDriver("old"));
        registry.Register("A", _ => new FakeDriver("new"));

        registry.Create("a", TestKit.EmptyProvider).Id.Should().Be("new");
        registry.Names.Should().HaveCount(1);
    }

    [Fact]
    public void Unknown_name_throws_and_lists_available_drivers()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        registry.Register("b", _ => new FakeDriver("b"));
        registry.Register("a", _ => new FakeDriver("a"));

        Action act = () => registry.Create("nope", TestKit.EmptyProvider);

        var exception = act.Should().Throw<DriverNotRegisteredException>().Which;
        exception.DriverName.Should().Be("nope");
        exception.AvailableDrivers.Should().BeEquivalentTo(new[] { "a", "b" });
        exception.Message.Should().Contain("a, b");
    }

    [Fact]
    public void Registered_event_is_raised_with_the_name()
    {
        var registry = new DriverRegistry<IFakeDriver>();
        var raised = new List<string>();
        registry.Registered += raised.Add;

        registry.Register("x", _ => new FakeDriver("x"));
        registry.Register("y", (sp, ct) => new ValueTask<IFakeDriver>(new FakeDriver("y")));

        raised.Should().BeEquivalentTo(new[] { "x", "y" });
    }

    [Fact]
    public void Blank_names_and_null_factories_are_rejected()
    {
        var registry = new DriverRegistry<IFakeDriver>();

        Action blank = () => registry.Register(" ", _ => new FakeDriver("x"));
        Action nullFactory = () => registry.Register("a", (Func<IServiceProvider, IFakeDriver>)null!);

        blank.Should().Throw<ArgumentException>();
        nullFactory.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Concurrent_registration_is_thread_safe()
    {
        var registry = new DriverRegistry<IFakeDriver>();

        Parallel.For(0, 1000, i => registry.Register($"driver-{i}", _ => new FakeDriver("x")));

        registry.Names.Should().HaveCount(1000);
    }
}
