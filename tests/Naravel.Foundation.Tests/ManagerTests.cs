namespace Naravel.Foundation.Tests;

public class ManagerTests
{
    // ---- resolution -------------------------------------------------------------------------------------------

    [Fact]
    public void Default_driver_comes_from_options()
    {
        var h = new ManagerHarness(TestKit.Options("b"));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Registry.Register("b", _ => new FakeDriver("b"));

        h.Manager.Driver().Id.Should().Be("b");
        h.Manager.DefaultDriverName.Should().Be("b");
    }

    [Fact]
    public void Explicit_name_overrides_the_default()
    {
        var h = new ManagerHarness(TestKit.Options("b"));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Registry.Register("b", _ => new FakeDriver("b"));

        h.Manager.Driver("a").Id.Should().Be("a");
    }

    [Fact]
    public void Repeated_calls_return_the_same_instance_case_insensitively()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));

        var first = h.Manager.Driver("a");

        h.Manager.Driver("A").Should().BeSameAs(first);
        h.Manager.Driver().Should().BeSameAs(first);
    }

    [Fact]
    public void Unknown_driver_throws_and_is_not_cached()
    {
        var h = new ManagerHarness();
        Action act = () => h.Manager.Driver("nope");

        act.Should().Throw<DriverNotRegisteredException>();

        h.Registry.Register("nope", _ => new FakeDriver("late"));
        h.Manager.Driver("nope").Id.Should().Be("late");
    }

    [Fact]
    public async Task Unknown_driver_throws_from_the_async_api_too()
    {
        var h = new ManagerHarness();
        Func<Task> act = async () => await h.Manager.DriverAsync("nope");

        await act.Should().ThrowAsync<DriverNotRegisteredException>();
    }

    [Fact]
    public void Missing_default_gives_an_actionable_error()
    {
        var h = new ManagerHarness(TestKit.Options(""));
        Action act = () => h.Manager.Driver();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Default*");
    }

    [Fact]
    public void Blank_explicit_name_is_rejected()
    {
        var h = new ManagerHarness();
        Action act = () => h.Manager.Driver("  ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Default_is_read_on_every_call_so_a_changed_default_applies_immediately()
    {
        var h = new ManagerHarness(TestKit.Options("a"));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Registry.Register("b", _ => new FakeDriver("b"));
        h.Manager.Driver().Id.Should().Be("a");

        h.Monitor.Publish(TestKit.Options("b"));

        h.Manager.Driver().Id.Should().Be("b");
    }

    // ---- creation guarantees ----------------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_callers_trigger_exactly_one_factory_call()
    {
        var h = new ManagerHarness();
        var calls = 0;
        var gate = new TaskCompletionSource();
        h.Registry.Register("a", async (sp, ct) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return new FakeDriver("a");
        });

        var callers = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => h.Manager.DriverAsync("a").AsTask()))
            .ToArray();
        await Task.Delay(50);
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        calls.Should().Be(1);
        results.All(r => ReferenceEquals(r, results[0])).Should().BeTrue();
    }

    [Fact]
    public void Concurrent_sync_callers_also_create_only_once()
    {
        var h = new ManagerHarness();
        var calls = 0;
        h.Registry.Register("a", _ =>
        {
            Interlocked.Increment(ref calls);
            Thread.Sleep(20);
            return new FakeDriver("a");
        });

        var results = new IFakeDriver[50];
        Parallel.For(0, 50, i => results[i] = h.Manager.Driver("a"));

        calls.Should().Be(1);
        results.All(r => ReferenceEquals(r, results[0])).Should().BeTrue();
    }

    [Fact]
    public void A_failed_sync_creation_is_not_cached()
    {
        var h = new ManagerHarness();
        var attempts = 0;
        h.Registry.Register("a", _ =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            return new FakeDriver("a");
        });

        Action first = () => h.Manager.Driver("a");
        first.Should().Throw<InvalidOperationException>().WithMessage("boom");

        h.Manager.Driver("a").Id.Should().Be("a");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task A_failed_async_creation_is_not_cached()
    {
        var h = new ManagerHarness();
        var attempts = 0;
        h.Registry.Register("a", async (sp, ct) =>
        {
            await Task.Yield();
            if (Interlocked.Increment(ref attempts) == 1)
            {
                throw new InvalidOperationException("boom");
            }

            return new FakeDriver("a");
        });

        Func<Task> first = async () => await h.Manager.DriverAsync("a");
        await first.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");

        var driver = await h.Manager.DriverAsync("a");
        driver.Id.Should().Be("a");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task An_already_cancelled_token_throws_immediately()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await h.Manager.DriverAsync("a", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancelling_one_caller_does_not_abort_the_shared_creation()
    {
        var h = new ManagerHarness();
        var calls = 0;
        var gate = new TaskCompletionSource();
        h.Registry.Register("a", async (sp, ct) =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return new FakeDriver("a");
        });
        using var cts = new CancellationTokenSource();
        var impatient = h.Manager.DriverAsync("a", cts.Token).AsTask();

        cts.Cancel();
        Func<Task> waitForImpatient = async () => await impatient;
        await waitForImpatient.Should().ThrowAsync<OperationCanceledException>();

        gate.SetResult();
        var driver = await h.Manager.DriverAsync("a");

        driver.Id.Should().Be("a");
        calls.Should().Be(1);
    }

    [Fact]
    public void Sync_api_rejects_an_async_only_driver_instead_of_blocking()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", (sp, ct) => new ValueTask<IFakeDriver>(new FakeDriver("a")));
        Action act = () => h.Manager.Driver("a");

        act.Should().Throw<InvalidOperationException>().WithMessage("*DriverAsync*");
    }

    [Fact]
    public async Task Sync_api_returns_an_async_driver_once_it_has_been_created()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", async (sp, ct) =>
        {
            await Task.Yield();
            return new FakeDriver("a");
        });

        var created = await h.Manager.DriverAsync("a");

        h.Manager.Driver("a").Should().BeSameAs(created);
    }

    // ---- runtime extension ------------------------------------------------------------------------------------

    [Fact]
    public void Extend_registers_a_driver_at_runtime()
    {
        var h = new ManagerHarness();
        h.Manager.Extend("custom", _ => new FakeDriver("custom"));

        h.Manager.Driver("custom").Id.Should().Be("custom");
    }

    [Fact]
    public async Task Extend_supports_async_factories()
    {
        var h = new ManagerHarness();
        h.Manager.Extend("custom", async (sp, ct) =>
        {
            await Task.Yield();
            return new FakeDriver("custom-async");
        });

        var driver = await h.Manager.DriverAsync("custom");

        driver.Id.Should().Be("custom-async");
    }

    [Fact]
    public void Replacing_a_factory_evicts_the_cached_instance()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("v1"));
        var first = h.Manager.Driver("a");

        h.Manager.Extend("a", _ => new FakeDriver("v2"));

        h.Manager.Driver("a").Id.Should().Be("v2");
        h.Manager.Driver("a").Should().NotBeSameAs(first);
    }

    [Fact]
    public void Registering_directly_on_the_registry_also_evicts()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("v1"));
        h.Manager.Driver("a");

        h.Registry.Register("a", _ => new FakeDriver("v2"));

        h.Manager.Driver("a").Id.Should().Be("v2");
    }

    // ---- configuration changes --------------------------------------------------------------------------------

    [Fact]
    public void A_real_change_to_the_stores_rebuilds_drivers()
    {
        var h = new ManagerHarness(TestKit.Options("a", ("a", "host", "1")));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var first = h.Manager.Driver("a");

        h.Monitor.Publish(TestKit.Options("a", ("a", "host", "2")));

        h.Manager.Driver("a").Should().NotBeSameAs(first);
    }

    [Fact]
    public void A_reload_with_identical_stores_keeps_cached_drivers()
    {
        var h = new ManagerHarness(TestKit.Options("a", ("a", "host", "1")));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var first = h.Manager.Driver("a");

        h.Monitor.Publish(TestKit.Options("a", ("a", "host", "1")));

        h.Manager.Driver("a").Should().BeSameAs(first);
    }

    [Fact]
    public void Changing_only_the_default_keeps_cached_drivers_but_switches_the_default()
    {
        var h = new ManagerHarness(TestKit.Options("a", ("a", "host", "1"), ("b", "host", "2")));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Registry.Register("b", _ => new FakeDriver("b"));
        var a = h.Manager.Driver("a");

        h.Monitor.Publish(TestKit.Options("b", ("a", "host", "1"), ("b", "host", "2")));

        h.Manager.Driver("a").Should().BeSameAs(a);
        h.Manager.Driver().Id.Should().Be("b");
    }

    [Fact]
    public void Changes_to_named_options_other_than_the_default_are_ignored()
    {
        var h = new ManagerHarness(TestKit.Options("a", ("a", "host", "1")));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var first = h.Manager.Driver("a");

        h.Monitor.Publish(TestKit.Options("a", ("a", "host", "2")), "some-other-name");

        h.Manager.Driver("a").Should().BeSameAs(first);
    }

    // ---- forgetting -------------------------------------------------------------------------------------------

    [Fact]
    public void Forget_drops_one_cached_driver()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var first = h.Manager.Driver("a");

        h.Manager.Forget("a").Should().BeTrue();
        h.Manager.Forget("a").Should().BeFalse();

        h.Manager.Driver("a").Should().NotBeSameAs(first);
    }

    [Fact]
    public void ForgetAll_drops_every_cached_driver()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Registry.Register("b", _ => new FakeDriver("b"));
        var a = h.Manager.Driver("a");
        var b = h.Manager.Driver("b");

        h.Manager.ForgetAll();

        h.Manager.Driver("a").Should().NotBeSameAs(a);
        h.Manager.Driver("b").Should().NotBeSameAs(b);
    }

    // ---- disposal ---------------------------------------------------------------------------------------------

    [Fact]
    public void Dispose_disposes_created_drivers_exactly_once()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var driver = (FakeDriver)h.Manager.Driver("a");

        h.Manager.Dispose();
        h.Manager.Dispose();

        driver.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_prefers_async_disposal_and_is_idempotent()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new AsyncFakeDriver("a"));
        var driver = (AsyncFakeDriver)h.Manager.Driver("a");

        await h.Manager.DisposeAsync();
        await h.Manager.DisposeAsync();

        driver.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void Sync_Dispose_also_handles_async_only_disposables()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new AsyncFakeDriver("a"));
        var driver = (AsyncFakeDriver)h.Manager.Driver("a");

        h.Manager.Dispose();

        driver.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void One_instance_registered_under_two_names_is_disposed_once()
    {
        var h = new ManagerHarness();
        var shared = new FakeDriver("shared");
        h.Registry.Register("x", _ => shared);
        h.Registry.Register("y", _ => shared);
        h.Manager.Driver("x");
        h.Manager.Driver("y");

        h.Manager.Dispose();

        shared.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void Retired_drivers_are_kept_alive_until_the_manager_is_disposed()
    {
        var h = new ManagerHarness(TestKit.Options("a", ("a", "host", "1")));
        h.Registry.Register("a", _ => new FakeDriver("a"));
        var old = (FakeDriver)h.Manager.Driver("a");

        h.Monitor.Publish(TestKit.Options("a", ("a", "host", "2")));
        var fresh = (FakeDriver)h.Manager.Driver("a");

        old.IsDisposed.Should().BeFalse("it may still be in use by a request that already holds it");
        h.Manager.Dispose();
        old.DisposeCalls.Should().Be(1);
        fresh.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_driver_forgotten_while_still_being_created_is_disposed_with_the_manager()
    {
        var h = new ManagerHarness();
        var gate = new TaskCompletionSource();
        h.Registry.Register("a", async (sp, ct) =>
        {
            await gate.Task;
            return new FakeDriver("a");
        });
        var pending = h.Manager.DriverAsync("a").AsTask();

        h.Manager.Forget("a").Should().BeTrue();
        gate.SetResult();
        var driver = (FakeDriver)await pending;
        h.Manager.Dispose();

        driver.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_creation_finishing_after_disposal_disposes_the_new_driver_and_fails_the_caller()
    {
        var h = new ManagerHarness();
        var gate = new TaskCompletionSource();
        FakeDriver? created = null;
        h.Registry.Register("a", async (sp, ct) =>
        {
            await gate.Task;
            created = new FakeDriver("a");
            return created;
        });
        var pending = h.Manager.DriverAsync("a").AsTask();

        h.Manager.Dispose();
        gate.SetResult();
        Func<Task> act = async () => await pending;

        await act.Should().ThrowAsync<ObjectDisposedException>();
        created.Should().NotBeNull();
        created!.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void Disposal_errors_are_aggregated_and_do_not_stop_other_drivers_from_disposing()
    {
        var h = new ManagerHarness();
        h.Registry.Register("bad", _ => new ThrowingDriver("bad"));
        h.Registry.Register("good", _ => new FakeDriver("good"));
        h.Manager.Driver("bad");
        var good = (FakeDriver)h.Manager.Driver("good");

        Action act = () => h.Manager.Dispose();

        act.Should().Throw<AggregateException>();
        good.DisposeCalls.Should().Be(1);
    }

    [Fact]
    public void Using_a_disposed_manager_throws()
    {
        var h = new ManagerHarness();
        h.Registry.Register("a", _ => new FakeDriver("a"));
        h.Manager.Dispose();
        Action act = () => h.Manager.Driver("a");

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Disposal_unsubscribes_from_the_options_monitor()
    {
        var h = new ManagerHarness();
        h.Monitor.ListenerCount.Should().Be(1);

        h.Manager.Dispose();

        h.Monitor.ListenerCount.Should().Be(0);
    }
}
