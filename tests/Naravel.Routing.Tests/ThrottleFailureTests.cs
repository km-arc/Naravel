using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Naravel.Routing.Tests;

/// <summary>
/// What the <c>throttle</c> middleware does when the registered <see cref="IThrottleStore"/> fails (PDR-024, R31.T02): the timeout,
/// the breaker, the three failure modes, the telemetry and the log. The store is a test double and the breaker's clock is a manual
/// <see cref="TimeProvider"/>, so no test waits for a real breaker period.
/// </summary>
public class ThrottleFailureTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // ----- the failure handling contract of PDR-024 -----

    [Fact]
    public async Task Degrade_to_local_limits_with_the_in_process_limiter_when_the_store_throws()
    {
        var store = new FlakyStore();
        await using var app = await StartAsync(store, new ManualTimeProvider());

        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be((HttpStatusCode)429);
        store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Degrade_to_local_when_the_store_exceeds_the_timeout()
    {
        var cancelled = NewSignal();
        var store = new FlakyStore
        {
            Behavior = async (_, _, _, ct) =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    cancelled.TrySetResult();
                    throw;
                }

                return default;
            }
        };
        await using var app = await StartAsync(store, new ManualTimeProvider(), o => o.Throttle.StoreTimeout = TimeSpan.FromMilliseconds(150));

        var clock = Stopwatch.StartNew();
        var first = await app.Client.GetAsync("/api");

        // The request is answered after about the timeout, not after the store (which would wait forever).
        (clock.Elapsed < Patience).Should().BeTrue();
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Task.WhenAny(cancelled.Task, Task.Delay(Patience))).Should().BeSameAs(cancelled.Task);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be((HttpStatusCode)429);
        store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_store_that_ignores_cancellation_still_times_out()
    {
        var never = NewSignal();
        var store = new FlakyStore { Behavior = async (_, _, _, _) => { await never.Task; return default; } };
        await using var app = await StartAsync(store, new ManualTimeProvider(), o => o.Throttle.StoreTimeout = TimeSpan.FromMilliseconds(100));

        var clock = Stopwatch.StartNew();
        var response = await app.Client.GetAsync("/api");

        (clock.Elapsed < Patience).Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        never.TrySetResult();
    }

    [Fact]
    public async Task Degraded_responses_keep_the_rate_limit_headers_and_count_the_degraded_outcome()
    {
        using var metrics = new Measurements();
        var store = new FlakyStore();
        await using var app = await StartAsync(
            store,
            new ManualTimeProvider(),
            o => o.ConfigureThrottlePolicy("t3", 2, Window),
            routes: r => r.Get("/t3", () => "ok").Middleware("throttle:t3"));

        var first = await app.Client.GetAsync("/t3");
        var second = await app.Client.GetAsync("/t3");
        var third = await app.Client.GetAsync("/t3");

        first.Headers.GetValues("X-RateLimit-Limit").Should().Equal("2");
        first.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("1");
        second.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");
        third.StatusCode.Should().Be((HttpStatusCode)429);
        third.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");
        third.Headers.Contains("Retry-After").Should().BeTrue();
        metrics.Sum("naravel.routing.throttle", "t3", "degraded").Should().Be(3);
        metrics.Sum("naravel.routing.throttle.store_failures", "t3").Should().Be(1);
    }

    [Fact]
    public async Task Breaker_opens_after_a_failure_and_skips_the_store_for_the_breaker_period()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);

        await app.Client.GetAsync("/api");
        for (var i = 0; i < 6; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(500));
            await app.Client.GetAsync("/api");
        }

        store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Breaker_probes_once_after_the_period_and_closes_when_the_probe_succeeds()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);
        await app.Client.GetAsync("/api");
        store.Calls.Should().Be(1);

        clock.Advance(TimeSpan.FromSeconds(6));
        store.Behavior = FlakyStore.Counting();
        var probe = await app.Client.GetAsync("/api");

        store.Calls.Should().Be(2);
        probe.StatusCode.Should().Be(HttpStatusCode.OK);
        probe.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("1");

        // Closed again: every request uses the store, and its answer (not the local limiter's) is what the caller gets.
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await app.Client.GetAsync("/api");
        rejected.StatusCode.Should().Be((HttpStatusCode)429);
        rejected.Headers.GetValues("Retry-After").Should().Equal("60");
        store.Calls.Should().Be(4);
    }

    [Fact]
    public async Task Breaker_reopens_for_another_period_when_the_probe_fails()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);
        await app.Client.GetAsync("/api");

        clock.Advance(TimeSpan.FromSeconds(6));
        await app.Client.GetAsync("/api");
        store.Calls.Should().Be(2);

        clock.Advance(TimeSpan.FromSeconds(4));
        for (var i = 0; i < 3; i++) await app.Client.GetAsync("/api");
        store.Calls.Should().Be(2);

        clock.Advance(TimeSpan.FromSeconds(2));
        await app.Client.GetAsync("/api");
        store.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Only_one_request_probes_a_half_open_breaker()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);
        await app.Client.GetAsync("/api");

        clock.Advance(TimeSpan.FromSeconds(6));
        var probeStarted = NewSignal();
        var release = NewSignal();
        store.Behavior = async (_, _, _, _) =>
        {
            probeStarted.TrySetResult();
            await release.Task;
            return new ThrottleDecision(true, 1, TimeSpan.Zero);
        };
        var probe = app.Client.GetAsync("/api");
        await probeStarted.Task.WaitAsync(Patience);

        var others = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => app.Client.GetAsync("/api")));

        // Only the probe reached the store (the first failure plus the probe). The others were handled locally: the request that
        // failed first already used one of the two local permits, so one of the ten is admitted and nine are rejected.
        store.Calls.Should().Be(2);
        others.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        others.Count(r => r.StatusCode == (HttpStatusCode)429).Should().Be(9);
        release.SetResult();
        (await probe).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Fail_closed_policy_answers_503_with_retry_after_and_never_uses_the_local_limiter()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);

        var first = await app.Client.GetAsync("/login");
        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        first.Headers.GetValues("Retry-After").Should().Equal("5");
        first.Headers.Contains("X-RateLimit-Limit").Should().BeFalse();
        first.Headers.Contains("X-RateLimit-Remaining").Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        var later = await app.Client.GetAsync("/login");
        later.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        later.Headers.GetValues("Retry-After").Should().Equal("3");

        // The policy allows 5 per window; the local limiter would have admitted five of these. It is never consulted.
        for (var i = 0; i < 8; i++)
        {
            (await app.Client.GetAsync("/login")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        }
    }

    [Fact]
    public async Task Fail_open_policy_admits_without_a_limit_when_the_store_fails()
    {
        await using var app = await StartAsync(new FlakyStore(), new ManualTimeProvider());

        for (var i = 0; i < 5; i++)
        {
            var response = await app.Client.GetAsync("/open");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("X-RateLimit-Limit").Should().BeFalse();
        }
    }

    [Fact]
    public async Task Failure_mode_is_per_policy_on_one_store()
    {
        var store = new FlakyStore();
        await using var app = await StartAsync(store, new ManualTimeProvider());

        (await app.Client.GetAsync("/login")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be((HttpStatusCode)429);
        (await app.Client.GetAsync("/login")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        store.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_client_abort_is_not_a_store_failure()
    {
        using var metrics = new Measurements();
        var logs = new CapturingLoggerProvider();
        var started = NewSignal();
        var sawAbort = NewSignal();
        var store = new FlakyStore
        {
            Behavior = async (_, _, _, ct) =>
            {
                started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    sawAbort.TrySetResult();
                    throw;
                }

                return default;
            }
        };
        await using var app = await StartAsync(
            store,
            new ManualTimeProvider(),
            o => o.ConfigureThrottlePolicy("t11", 2, Window),
            services: s => s.AddLogging(b => b.AddProvider(logs)),
            routes: r => r.Get("/t11", () => "ok").Middleware("throttle:t11"));

        using var cts = new CancellationTokenSource();
        var pending = app.Client.GetAsync("/t11", cts.Token);
        await started.Task.WaitAsync(Patience);
        cts.Cancel();
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
        }

        await sawAbort.Task.WaitAsync(Patience);

        // The breaker did not open: the next request goes to the store and is not degraded.
        store.Behavior = FlakyStore.Counting();
        var after = await app.Client.GetAsync("/t11");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        store.Calls.Should().Be(2);
        metrics.Sum("naravel.routing.throttle.store_failures", "t11").Should().Be(0);
        metrics.Sum("naravel.routing.throttle", "t11", "degraded").Should().Be(0);
        logs.Count(LogLevel.Warning, "ThrottleGuard").Should().Be(0);
    }

    [Fact]
    public async Task The_breaker_opening_is_logged_once_per_period()
    {
        using var metrics = new Measurements();
        var logs = new CapturingLoggerProvider();
        var clock = new ManualTimeProvider();
        var gate = NewSignal();
        var store = new FlakyStore
        {
            Behavior = async (_, _, _, _) =>
            {
                await gate.Task;
                throw new InvalidOperationException("the store is down");
            }
        };
        await using var app = await StartAsync(
            store,
            clock,
            o => o.ConfigureThrottlePolicy("t12", 2, Window),
            services: s => s.AddLogging(b => b.AddProvider(logs)),
            routes: r => r.Get("/t12", () => "ok").Middleware("throttle:t12"));

        // Eight requests are inside the store call when it starts failing: every failure is counted, the opening is logged once.
        var inFlight = Enumerable.Range(0, 8).Select(_ => app.Client.GetAsync("/t12")).ToArray();
        await WaitUntilAsync(() => store.Calls == 8);
        gate.SetResult();
        await Task.WhenAll(inFlight);

        logs.Count(LogLevel.Warning, "ThrottleGuard").Should().Be(1);
        metrics.Sum("naravel.routing.throttle.store_failures", "t12").Should().Be(8);

        // A new period starts with the failed probe, and that opening is logged again.
        clock.Advance(TimeSpan.FromSeconds(6));
        await app.Client.GetAsync("/t12");
        logs.Count(LogLevel.Warning, "ThrottleGuard").Should().Be(2);
    }

    [Fact]
    public async Task Recovery_does_not_replay_locally_admitted_requests_into_the_store()
    {
        var clock = new ManualTimeProvider();
        var store = new FlakyStore();
        await using var app = await StartAsync(store, clock);

        var local1 = await app.Client.GetAsync("/api");
        var local2 = await app.Client.GetAsync("/api");
        local1.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("1");
        local2.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");

        clock.Advance(TimeSpan.FromSeconds(6));
        store.Behavior = FlakyStore.Counting();

        // The store starts from its own state: had the two local admissions been replayed it would already be over the limit of 2.
        var probe = await app.Client.GetAsync("/api");
        probe.StatusCode.Should().Be(HttpStatusCode.OK);
        probe.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("1");
        (await app.Client.GetAsync("/api")).Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");
        (await app.Client.GetAsync("/api")).StatusCode.Should().Be((HttpStatusCode)429);
    }

    // ----- options -----

    [Fact]
    public void Throttle_options_have_the_approved_defaults_and_reject_values_that_are_not_positive()
    {
        var options = new RoutingOptions();

        options.Throttle.StoreTimeout.Should().Be(TimeSpan.FromMilliseconds(250));
        options.Throttle.BreakerPeriod.Should().Be(TimeSpan.FromSeconds(5));

        Action zeroTimeout = () => options.Throttle.StoreTimeout = TimeSpan.Zero;
        Action negativePeriod = () => options.Throttle.BreakerPeriod = TimeSpan.FromSeconds(-1);
        Action unknownMode = () => options.ConfigureThrottlePolicy("x", 1, Window, (ThrottleFailureMode)7);
        zeroTimeout.Should().Throw<ArgumentOutOfRangeException>();
        negativePeriod.Should().Throw<ArgumentOutOfRangeException>();
        unknownMode.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- test support -----

    private static Task<TestApp> StartAsync(
        FlakyStore store,
        ManualTimeProvider clock,
        Action<RoutingOptions>? options = null,
        Action<IServiceCollection>? services = null,
        Action<IRouteRegistrar>? routes = null) =>
        TestApp.StartAsync(
            routes: routes ?? CommonRoutes,
            options: o =>
            {
                o.ConfigureThrottlePolicy("login", 5, Window, ThrottleFailureMode.FailClosed);
                o.ConfigureThrottlePolicy("api", 2, Window);
                o.ConfigureThrottlePolicy("open", 1, Window, ThrottleFailureMode.FailOpen);

                // Long, so a call that is held open on purpose (a probe, a gate) is never cut off by the timeout; the tests that
                // are about the timeout set their own short value.
                o.Throttle.StoreTimeout = TimeSpan.FromSeconds(30);
                options?.Invoke(o);
            },
            services: s =>
            {
                s.AddSingleton<IThrottleStore>(store);
                s.AddSingleton<TimeProvider>(clock);
                services?.Invoke(s);
            });

    private static void CommonRoutes(IRouteRegistrar r)
    {
        r.Get("/login", () => "ok").Middleware("throttle:login");
        r.Get("/api", () => "ok").Middleware("throttle:api");
        r.Get("/open", () => "ok").Middleware("throttle:open");
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            if (deadline.Elapsed > Patience) throw new TimeoutException("The condition did not become true in time.");
            await Task.Delay(10);
        }
    }

    /// <summary>A store that fails until a test gives it another behavior, and counts its calls.</summary>
    private sealed class FlakyStore : IThrottleStore
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Func<string, int, TimeSpan, CancellationToken, ValueTask<ThrottleDecision>> Behavior { get; set; } =
            static (_, _, _, _) => throw new InvalidOperationException("the store is down");

        public ValueTask<ThrottleDecision> AttemptAsync(string partitionKey, int permitLimit, TimeSpan window, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Behavior(partitionKey, permitLimit, window, cancellationToken);
        }

        /// <summary>A healthy fixed-window counter, the shape a shared store has.</summary>
        public static Func<string, int, TimeSpan, CancellationToken, ValueTask<ThrottleDecision>> Counting()
        {
            var counts = new Dictionary<string, int>();
            return (key, limit, window, _) =>
            {
                int count;
                lock (counts)
                {
                    counts.TryGetValue(key, out count);
                    counts[key] = ++count;
                }

                return ValueTask.FromResult(new ThrottleDecision(count <= limit, Math.Max(0, limit - count), count <= limit ? TimeSpan.Zero : window));
            };
        }
    }

    /// <summary>A clock a test moves by hand; only the breaker reads it.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public void Advance(TimeSpan by)
        {
            lock (_gate) _now += by;
        }
    }

    /// <summary>Collects the <c>Naravel.Routing</c> counters. Tests filter by a policy name of their own because the meter is process-wide.</summary>
    private sealed class Measurements : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Instrument, string Policy, string? Outcome, long Value)> _seen = new();

        public Measurements()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Naravel.Routing") listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                string? policy = null, outcome = null;
                foreach (var tag in tags)
                {
                    if (tag.Key == "policy") policy = tag.Value as string;
                    if (tag.Key == "outcome") outcome = tag.Value as string;
                }

                lock (_seen) _seen.Add((instrument.Name, policy ?? "", outcome, value));
            });
            _listener.Start();
        }

        public long Sum(string instrument, string policy, string? outcome = null)
        {
            lock (_seen)
            {
                return _seen
                    .Where(m => m.Instrument == instrument && m.Policy == policy && (outcome is null || m.Outcome == outcome))
                    .Sum(m => m.Value);
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(string Category, LogLevel Level)> _entries = new();

        public int Count(LogLevel level, string categoryContains)
        {
            lock (_entries) return _entries.Count(e => e.Level == level && e.Category.Contains(categoryContains, StringComparison.Ordinal));
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries) owner._entries.Add((category, logLevel));
            }
        }
    }
}
