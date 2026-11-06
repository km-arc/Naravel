using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Testing;

namespace Naravel.Routing.Cache.Tests;

/// <summary>
/// What every <see cref="IThrottleStore"/> must do, whichever backend counts (PDR-024): the in-process default, the Cache adapter over the
/// Memory-style store, and the adapter over a real Redis. Every case uses a partition key of its own, so a store that keeps state between
/// runs (Redis) does not leak one case into another.
/// </summary>
public abstract class ThrottleStoreContract : IDisposable
{
    private readonly List<IDisposable> _owned = [];

    protected abstract IThrottleStore Create();

    protected T Own<T>(T disposable) where T : IDisposable
    {
        _owned.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        foreach (var owned in _owned) owned.Dispose();
    }

    private static string Partition(string name) => $"contract|/{name}|{Guid.NewGuid():N}";

    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    protected async Task Counts_up_to_the_limit_then_rejects_Core()
    {
        var store = Create();
        var key = Partition("limit");

        var first = await store.AttemptAsync(key, 3, Window, CancellationToken.None);
        var second = await store.AttemptAsync(key, 3, Window, CancellationToken.None);
        var third = await store.AttemptAsync(key, 3, Window, CancellationToken.None);
        var fourth = await store.AttemptAsync(key, 3, Window, CancellationToken.None);
        var fifth = await store.AttemptAsync(key, 3, Window, CancellationToken.None);

        new[] { first.Allowed, second.Allowed, third.Allowed, fourth.Allowed, fifth.Allowed }
            .Should().Equal(true, true, true, false, false);
        new[] { first.Remaining, second.Remaining, third.Remaining, fourth.Remaining, fifth.Remaining }
            .Should().Equal(2, 1, 0, 0, 0);
        first.RetryAfter.Should().Be(TimeSpan.Zero);
        third.RetryAfter.Should().Be(TimeSpan.Zero);
        (fourth.RetryAfter > TimeSpan.Zero && fourth.RetryAfter <= Window).Should().BeTrue("a rejected caller is told when to come back, within one window");
    }

    protected async Task Partitions_are_counted_independently_Core()
    {
        var store = Create();
        var a = Partition("a");
        var b = Partition("b");

        (await store.AttemptAsync(a, 1, Window, CancellationToken.None)).Allowed.Should().BeTrue();
        (await store.AttemptAsync(b, 1, Window, CancellationToken.None)).Allowed.Should().BeTrue();
        (await store.AttemptAsync(a, 1, Window, CancellationToken.None)).Allowed.Should().BeFalse();
        (await store.AttemptAsync(b, 1, Window, CancellationToken.None)).Allowed.Should().BeFalse();
    }

    protected async Task A_limit_of_one_admits_exactly_one_Core()
    {
        var store = Create();
        var key = Partition("one");

        var decisions = new List<bool>();
        for (var i = 0; i < 5; i++) decisions.Add((await store.AttemptAsync(key, 1, Window, CancellationToken.None)).Allowed);

        decisions.Should().Equal(true, false, false, false, false);
    }

    protected async Task Concurrent_attempts_admit_exactly_the_limit_Core()
    {
        var store = Create();
        var key = Partition("burst");

        var decisions = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => store.AttemptAsync(key, 10, Window, CancellationToken.None).AsTask())));

        decisions.Count(d => d.Allowed).Should().Be(10);
        decisions.Count(d => !d.Allowed).Should().Be(90);
        decisions.Where(d => !d.Allowed).Should().OnlyContain(d => d.Remaining == 0);
    }
}

public sealed class InProcessStoreContractTests : ThrottleStoreContract
{
    protected override IThrottleStore Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelRouting();
        return Own(services.BuildServiceProvider()).GetRequiredService<IThrottleStore>();
    }

    [Fact]
    public Task Counts_up_to_the_limit_then_rejects() => Counts_up_to_the_limit_then_rejects_Core();

    [Fact]
    public Task Partitions_are_counted_independently() => Partitions_are_counted_independently_Core();

    [Fact]
    public Task A_limit_of_one_admits_exactly_one() => A_limit_of_one_admits_exactly_one_Core();

    [Fact]
    public Task Concurrent_attempts_admit_exactly_the_limit() => Concurrent_attempts_admit_exactly_the_limit_Core();
}

public sealed class MemoryStoreContractTests : ThrottleStoreContract
{
    // The real Memory store of Naravel.Cache. The bucket comes from a clock frozen half way into a minute, so no case can meet a window
    // boundary by chance; the Memory store itself expires entries on the system clock, which a one-hour window never reaches.
    protected override IThrottleStore Create() =>
        Backends.Throttle(Own(Backends.MemoryProvider(new ManualClock(Backends.MidMinute))));

    [Fact]
    public Task Counts_up_to_the_limit_then_rejects() => Counts_up_to_the_limit_then_rejects_Core();

    [Fact]
    public Task Partitions_are_counted_independently() => Partitions_are_counted_independently_Core();

    [Fact]
    public Task A_limit_of_one_admits_exactly_one() => A_limit_of_one_admits_exactly_one_Core();

    [Fact]
    public Task Concurrent_attempts_admit_exactly_the_limit() => Concurrent_attempts_admit_exactly_the_limit_Core();
}

public sealed class CacheFakeStoreContractTests : ThrottleStoreContract
{
    protected override IThrottleStore Create() =>
        Backends.Throttle(Own(Backends.Provider(new CacheFake("fake"), new ManualClock(Backends.MidMinute))));

    [Fact]
    public Task Counts_up_to_the_limit_then_rejects() => Counts_up_to_the_limit_then_rejects_Core();

    [Fact]
    public Task Partitions_are_counted_independently() => Partitions_are_counted_independently_Core();

    [Fact]
    public Task A_limit_of_one_admits_exactly_one() => A_limit_of_one_admits_exactly_one_Core();

    [Fact]
    public Task Concurrent_attempts_admit_exactly_the_limit() => Concurrent_attempts_admit_exactly_the_limit_Core();
}
