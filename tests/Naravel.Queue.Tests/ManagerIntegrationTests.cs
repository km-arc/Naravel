using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Foundation;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Memory;

namespace Naravel.Queue.Tests;

/// <summary>
/// Covers the PDR-006 migration (Queue on top of Naravel.Foundation): the mandatory runtime-<c>Extend</c>
/// test and config-reload test from AGENTS.md hard rule 5, plus a regression test for the multi-store
/// question raised while designing PDR-005 ("can two stores share one driver type?").
/// </summary>
public class ManagerIntegrationTests
{
    private static ServiceProvider Build(ConfigurationManager config, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueue(config);
        extra?.Invoke(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Two_stores_can_use_the_same_driver_type_with_different_settings()
    {
        // The scenario PDR-005 had to account for: "high" and "low" are both memory stores (same driver
        // type, different names) - each must get its own isolated queue state.
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "high",
            ["NaravelQueue:Stores:high:Driver"] = "memory",
            ["NaravelQueue:Stores:low:Driver"] = "memory",
        });
        using var provider = Build(config, s => s.AddMemoryDriver(config));
        var manager = provider.GetRequiredService<QueueManager>();

        await manager.Connection("high").PushAsync(new QueuedMessage { Queue = "q", JobType = "t", Payload = "{}" });

        (await manager.Connection("low").PopAsync("q")).Should().BeNull("the two stores must not share state just because they use the same driver");
        (await manager.Connection("high").PopAsync("q")).Should().NotBeNull();
    }

    [Fact]
    public void Runtime_Extend_registers_a_driver_the_manager_did_not_know_about_at_startup()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["NaravelQueue:Default"] = "custom" });
        using var provider = Build(config);
        var manager = provider.GetRequiredService<QueueManager>();
        var fake = new FakeDriver();

        manager.Extend("custom", _ => fake);

        manager.Connection("custom").Should().BeSameAs(fake);
    }

    [Fact]
    public void A_config_reload_that_changes_a_store_rebuilds_its_driver()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "a",
            ["NaravelQueue:Stores:a:Driver"] = "memory",
        });
        using var provider = Build(config, s => s.AddMemoryDriver(config));
        var manager = provider.GetRequiredService<QueueManager>();
        var first = manager.Connection("a");

        // Change something inside the "a" store's section and reload - Manager compares a fingerprint of
        // every store's key/value pairs (Naravel.Foundation, PDR-004) and rebuilds only when it really changed.
        config["NaravelQueue:Stores:a:SomeSetting"] = "changed";
        ((IConfigurationRoot)config).Reload();

        manager.Connection("a").Should().NotBeSameAs(first, "a store's configuration changed, so its driver must be rebuilt");
    }

    [Fact]
    public void An_unrelated_reload_does_not_rebuild_any_driver()
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "a",
            ["NaravelQueue:Stores:a:Driver"] = "memory",
            ["Unrelated:Setting"] = "1",
        });
        using var provider = Build(config, s => s.AddMemoryDriver(config));
        var manager = provider.GetRequiredService<QueueManager>();
        var first = manager.Connection("a");

        config["Unrelated:Setting"] = "2";
        ((IConfigurationRoot)config).Reload();

        manager.Connection("a").Should().BeSameAs(first, "nothing in NaravelQueue's own stores changed");
    }

    private sealed class FakeDriver : IQueueDriver
    {
        public Task PushAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<QueuedMessage?> PopAsync(string queue, CancellationToken ct = default) => Task.FromResult<QueuedMessage?>(null);
        public Task AckAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken ct = default) => Task.CompletedTask;
        public Task FailAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> SizeAsync(string queue, CancellationToken ct = default) => Task.FromResult(0L);
    }
}
