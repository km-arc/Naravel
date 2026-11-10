using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Routing.Tests;

/// <summary>
/// The store seam of the <c>throttle</c> middleware (PDR-024, R31.T01): the middleware counts through whatever
/// <see cref="IThrottleStore"/> is registered, and the default store counts in process like before the seam existed.
/// </summary>
public class ThrottleStoreTests
{
    [Fact]
    public async Task Throttle_counts_through_the_registered_store_with_the_policy_numbers()
    {
        var store = new ScriptedStore((_, limit, _) => new ThrottleDecision(true, limit - 1, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(
            routes: r => r.Get("/limited", () => "ok").Middleware("throttle:5,2"),
            services: s => s.AddSingleton<IThrottleStore>(store));

        var response = await app.Client.GetAsync("/limited");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-RateLimit-Limit").Should().Equal("5");
        response.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("4");
        store.Calls.Should().HaveCount(1);
        store.Calls[0].Limit.Should().Be(5);
        store.Calls[0].Window.Should().Be(TimeSpan.FromMinutes(2));
        store.Calls[0].Key.Should().StartWith("3:5:2|");
    }

    [Fact]
    public async Task Partitions_whose_fields_would_join_to_the_same_text_reach_the_store_under_different_keys()
    {
        // The policy 5:2 on the endpoint "a|b" for the subject "c", and on the endpoint "a" for the subject "b|c": joined with
        // a plain "|" both read 5:2|a|b|c, so two clients would have shared one counter (AUDIT D-87).
        var store = new ScriptedStore((_, limit, _) => new ThrottleDecision(true, limit - 1, TimeSpan.Zero));
        await using var app = await TestApp.StartAsync(
            routes: r =>
            {
                r.Get("/one", () => "1").Name("a|b").Middleware("test.user", "throttle:5,2");
                r.Get("/two", () => "2").Name("a").Middleware("test.user", "throttle:5,2");
            },
            options: o => o.Middleware.Alias("test.user", async (context, next, _) =>
            {
                var user = context.Request.Headers["X-User"].ToString();
                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user)], "test"));
                await next(context);
            }),
            services: s => s.AddSingleton<IThrottleStore>(store));

        await GetAsUserAsync(app, "/one", "c");
        await GetAsUserAsync(app, "/two", "b|c");

        var calls = store.Calls;
        calls.Should().HaveCount(2);
        calls[0].Key.Should().NotBe(calls[1].Key);
    }

    [Fact]
    public async Task A_rejection_from_the_store_answers_429_with_Retry_After_and_does_not_run_the_endpoint()
    {
        var hits = 0;
        var store = new ScriptedStore((_, _, _) => new ThrottleDecision(false, 0, TimeSpan.FromSeconds(42)));
        await using var app = await TestApp.StartAsync(
            routes: r => r.Get("/limited", () =>
            {
                Interlocked.Increment(ref hits);
                return "ok";
            }).Middleware("throttle:3,1"),
            services: s => s.AddSingleton<IThrottleStore>(store));

        var response = await app.Client.GetAsync("/limited");

        response.StatusCode.Should().Be((HttpStatusCode)429);
        response.Headers.GetValues("Retry-After").Should().Equal("42");
        response.Headers.GetValues("X-RateLimit-Limit").Should().Equal("3");
        response.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");
        hits.Should().Be(0);
    }

    [Fact]
    public async Task Retry_After_is_whole_seconds_rounded_up_and_never_below_one()
    {
        foreach (var (seconds, expected) in new[] { (0.2, "1"), (1.0, "1"), (1.2, "2"), (59.01, "60") })
        {
            var store = new ScriptedStore((_, _, _) => new ThrottleDecision(false, 0, TimeSpan.FromSeconds(seconds)));
            await using var app = await TestApp.StartAsync(
                routes: r => r.Get("/limited", () => "ok").Middleware("throttle:1,1"),
                services: s => s.AddSingleton<IThrottleStore>(store));

            var response = await app.Client.GetAsync("/limited");

            response.Headers.GetValues("Retry-After").Should().Equal(expected);
        }
    }

    [Fact]
    public async Task The_default_store_and_a_counting_store_give_the_same_status_and_headers()
    {
        var inProcess = await RunFourRequestsAsync(services: null);
        var counting = await RunFourRequestsAsync(s => s.AddSingleton<IThrottleStore>(CountingStore()));

        inProcess.Should().Equal(
            "200 limit=2 remaining=1 retry-after=False",
            "200 limit=2 remaining=0 retry-after=False",
            "429 limit=2 remaining=0 retry-after=True",
            "429 limit=2 remaining=0 retry-after=True");
        counting.Should().Equal(inProcess);
    }

    [Fact]
    public async Task The_default_store_counts_in_process_per_partition()
    {
        var services = new ServiceCollection();
        services.AddNaravelRouting();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IThrottleStore>();
        var window = TimeSpan.FromMinutes(1);

        var first = await store.AttemptAsync("p|/x|a", 2, window, CancellationToken.None);
        var second = await store.AttemptAsync("p|/x|a", 2, window, CancellationToken.None);
        var third = await store.AttemptAsync("p|/x|a", 2, window, CancellationToken.None);
        var otherSubject = await store.AttemptAsync("p|/x|b", 2, window, CancellationToken.None);

        first.Should().Be(new ThrottleDecision(true, 1, TimeSpan.Zero));
        second.Should().Be(new ThrottleDecision(true, 0, TimeSpan.Zero));
        third.Allowed.Should().BeFalse();
        third.Remaining.Should().Be(0);
        (third.RetryAfter > TimeSpan.Zero && third.RetryAfter <= window).Should().BeTrue();
        otherSubject.Allowed.Should().BeTrue();
    }

    [Fact]
    public void A_store_registered_before_AddNaravelRouting_is_kept()
    {
        var store = new ScriptedStore((_, _, _) => new ThrottleDecision(true, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddSingleton<IThrottleStore>(store);
        services.AddNaravelRouting();
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThrottleStore>().Should().BeSameAs(store);
    }

    private static async Task GetAsUserAsync(TestApp app, string url, string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-User", user);
        using var response = await app.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<List<string>> RunFourRequestsAsync(Action<IServiceCollection>? services)
    {
        await using var app = await TestApp.StartAsync(
            routes: r => r.Get("/limited", () => "ok").Middleware("throttle:2,1"),
            services: services);

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var response = await app.Client.GetAsync("/limited");
            var limit = string.Join(",", response.Headers.GetValues("X-RateLimit-Limit"));
            var remaining = string.Join(",", response.Headers.GetValues("X-RateLimit-Remaining"));
            seen.Add($"{(int)response.StatusCode} limit={limit} remaining={remaining} retry-after={response.Headers.Contains("Retry-After")}");
        }

        return seen;
    }

    /// <summary>A fixed-window counter over a dictionary, the shape a shared store has.</summary>
    private static ScriptedStore CountingStore()
    {
        var counts = new Dictionary<string, int>();
        return new ScriptedStore((key, limit, window) =>
        {
            int count;
            lock (counts)
            {
                counts.TryGetValue(key, out count);
                counts[key] = ++count;
            }

            return new ThrottleDecision(count <= limit, Math.Max(0, limit - count), count <= limit ? TimeSpan.Zero : window);
        });
    }

    /// <summary>Records every attempt and answers with a scripted decision.</summary>
    private sealed class ScriptedStore(Func<string, int, TimeSpan, ThrottleDecision> decide) : IThrottleStore
    {
        private readonly List<(string Key, int Limit, TimeSpan Window)> _calls = new();

        public IReadOnlyList<(string Key, int Limit, TimeSpan Window)> Calls
        {
            get
            {
                lock (_calls) return _calls.ToArray();
            }
        }

        public ValueTask<ThrottleDecision> AttemptAsync(
            string partitionKey,
            int permitLimit,
            TimeSpan window,
            CancellationToken cancellationToken)
        {
            lock (_calls) _calls.Add((partitionKey, permitLimit, window));
            return ValueTask.FromResult(decide(partitionKey, permitLimit, window));
        }
    }
}
