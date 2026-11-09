using Microsoft.Extensions.Options;

namespace Naravel.Routing.Tests;

/// <summary>
/// The state machine of the circuit breaker around the throttle store (PDR-024, R31.T02), tested without HTTP and with a clock that a
/// test moves by hand. <c>ThrottleFailureTests</c> reaches the breaker only through whole requests; these tests pin the transitions that
/// are hard to reach that way, above all the probe whose client goes away: if the breaker did not hand that probe on, it would stay
/// half-open for good, the store would never be called again and every request would be handled as if the store were down. The last
/// tests of the breaker alone move the system clock while the process runs: the period is measured on the monotonic timestamp.
/// </summary>
public class ThrottleBreakerTests
{
    private const string Key = "t|/t|203.0.113.7";
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(5);
    // Past the end of the period, so that these tests do not depend on how the exact instant of the boundary is treated; the first
    // test pins that instant.
    private static readonly TimeSpan AfterPeriod = Period + TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);
    private static readonly RoutingOptions.ThrottlePolicy Policy = new(2, TimeSpan.FromMinutes(1));

    // ----- the breaker alone -----

    [Fact]
    public void A_failure_opens_the_breaker_for_the_whole_period_and_then_one_call_probes()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
        breaker.RemainingOpen().Should().Be(TimeSpan.Zero);

        breaker.Failed(probe: false).Should().BeTrue("the failure that opens the breaker is the one that is logged");
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        breaker.RemainingOpen().Should().Be(Period);

        clock.Advance(Period - TimeSpan.FromMilliseconds(1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open, "the period has not passed yet");
        breaker.RemainingOpen().Should().Be(TimeSpan.FromMilliseconds(1));

        clock.Advance(TimeSpan.FromMilliseconds(1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe, "the period is over");
    }

    [Fact]
    public void While_a_probe_is_running_every_other_call_is_handled_as_open()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);
        clock.Advance(AfterPeriod);

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        breaker.RemainingOpen().Should().Be(TimeSpan.Zero, "a fail-closed answer never asks a caller to wait for a period that is over");
    }

    [Fact]
    public void A_probe_that_succeeds_closes_the_breaker()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);
        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);

        breaker.Succeeded(probe: true);

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
    }

    [Fact]
    public void A_probe_that_fails_opens_the_breaker_for_another_whole_period_and_the_next_probe_is_possible()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);
        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);

        breaker.Failed(probe: true).Should().BeTrue("a failed probe reopens the breaker, which is logged again");

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        breaker.RemainingOpen().Should().Be(Period);
        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe, "the failed probe does not keep the probe slot");
    }

    [Fact]
    public void A_probe_that_the_client_abandons_is_not_a_failure_and_the_next_call_probes()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);
        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);

        breaker.Abandoned(probe: true);

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe, "an abandoned probe says nothing about the store, so it is handed on");
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open, "and again only one call probes");
    }

    [Fact]
    public void An_ordinary_call_that_is_abandoned_does_not_take_the_probe_from_the_call_that_holds_it()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);
        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);

        // A call that began before the breaker opened ends because its client went away.
        breaker.Abandoned(probe: false);

        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open, "the probe is still running");
    }

    [Fact]
    public void A_call_that_began_before_the_breaker_opened_can_neither_close_it_nor_extend_it()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Closed);

        breaker.Failed(probe: false).Should().BeTrue();
        clock.Advance(TimeSpan.FromSeconds(2));

        breaker.Failed(probe: false).Should().BeFalse("a second failure of calls that were already running is not logged again");
        breaker.RemainingOpen().Should().Be(TimeSpan.FromSeconds(3), "and it does not move the end of the period");

        breaker.Succeeded(probe: false);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open, "a late answer of an ordinary call does not close the breaker");
    }

    [Fact]
    public void A_system_clock_that_is_set_back_does_not_keep_the_breaker_open_for_longer_than_the_period()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);

        // A time synchronization step, a virtual machine that resumes, a person who sets the clock: the date and time of day go back
        // an hour while the process keeps running.
        clock.StepSystemClock(TimeSpan.FromHours(-1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        breaker.RemainingOpen().Should().Be(Period, "the step does not lengthen the period that is left");

        clock.Advance(AfterPeriod);
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe, "the period is over after its own length, not after an hour");
    }

    [Fact]
    public void A_system_clock_that_is_set_forward_does_not_end_the_period_early()
    {
        var clock = new ManualTimeProvider();
        var breaker = new ThrottleBreaker(clock, Period);
        breaker.Failed(probe: false);

        clock.StepSystemClock(TimeSpan.FromHours(1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open, "the store is still not called in the period that has not passed");
        breaker.RemainingOpen().Should().Be(Period, "the step does not shorten the period that is left");

        clock.Advance(Period - TimeSpan.FromMilliseconds(1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Open);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        breaker.TryEnter().Should().Be(ThrottleBreaker.Entry.Probe);
    }

    // ----- the guard that drives it -----

    [Fact]
    public async Task A_probe_whose_client_goes_away_leaves_the_next_request_to_probe_and_the_breaker_closes_after_it()
    {
        var clock = new ManualTimeProvider();
        var store = new ScriptedStore();
        using var guard = NewGuard(store, clock);

        // The store is down: the request is degraded to the local limiter and the breaker opens.
        var first = await guard.AttemptAsync(Key, "t", Policy, CancellationToken.None);
        first.Kind.Should().Be(ThrottleAttemptKind.Decided);
        store.Calls.Should().Be(1);

        clock.Advance(AfterPeriod);

        // The probe: the store waits until its token is cancelled, and then the client of the request goes away.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Behavior = async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default;
        };
        using var clientGone = new CancellationTokenSource();
        var probe = guard.AttemptAsync(Key, "t", Policy, clientGone.Token).AsTask();
        await started.Task.WaitAsync(Patience);
        store.Calls.Should().Be(2);
        clientGone.Cancel();

        var cancelled = false;
        try
        {
            await probe.WaitAsync(Patience);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        cancelled.Should().BeTrue("the client's cancellation ends the request");

        // The store is back. Nothing failed, so the breaker is still waiting for a probe: the next request is it, and the store answers.
        store.Behavior = _ => ValueTask.FromResult(new ThrottleDecision(true, 99, TimeSpan.Zero));
        var second = await guard.AttemptAsync(Key, "t", Policy, CancellationToken.None);
        second.Decision.Remaining.Should().Be(99, "the answer came from the store, not from the local limiter");
        store.Calls.Should().Be(3);

        // The probe succeeded, so the breaker is closed and the store is used again.
        var third = await guard.AttemptAsync(Key, "t", Policy, CancellationToken.None);
        third.Decision.Remaining.Should().Be(99);
        store.Calls.Should().Be(4);
    }

    private static ThrottleGuard NewGuard(ScriptedStore store, ManualTimeProvider clock)
    {
        var options = new RoutingOptions();
        options.Throttle.BreakerPeriod = Period;
        // A real timeout never ends a call here: the tests end it with a cancellation.
        options.Throttle.StoreTimeout = TimeSpan.FromMinutes(5);
        return new ThrottleGuard(store, Options.Create(options), time: clock);
    }

    /// <summary>A store that is down until a test gives it another behavior, and counts its calls.</summary>
    private sealed class ScriptedStore : IThrottleStore
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Func<CancellationToken, ValueTask<ThrottleDecision>> Behavior { get; set; } =
            static _ => throw new InvalidOperationException("the store is down");

        public ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Behavior(cancellationToken);
        }
    }

    /// <summary>
    /// A clock a test moves by hand; only the breaker reads it. <see cref="Advance"/> is time passing: it moves the date and time of
    /// day and the monotonic timestamp. <see cref="StepSystemClock"/> moves the date and time of day only, as a change of the system
    /// clock does.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public override long GetTimestamp()
        {
            lock (_gate) return _timestamp;
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate)
            {
                _now += by;
                _timestamp += by.Ticks;
            }
        }

        public void StepSystemClock(TimeSpan by)
        {
            lock (_gate) _now += by;
        }
    }
}
