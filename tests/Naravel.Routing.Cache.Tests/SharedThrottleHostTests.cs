using System.Net;
using Naravel.Cache.Testing;
using Naravel.Routing.Tests;

namespace Naravel.Routing.Cache.Tests;

/// <summary>Whole application hosts on the shared throttle: the quota is shared, restarts keep it, the headers match the default store and an outage is handled by the policy.</summary>
public sealed class SharedThrottleHostTests
{
    private static void Limited(IRouteRegistrar r) => r.Get("/limited", () => "ok").Middleware("throttle:1,1");

    private static void Limited2(IRouteRegistrar r) => r.Get("/limited", () => "ok").Middleware("throttle:2,1");

    [Fact]
    public async Task Two_hosts_on_one_shared_store_share_one_quota_and_a_restarted_host_keeps_it()
    {
        // The per-instance test of the Routing tests with the expectation inverted: two hosts stand for two servers behind a load balancer.
        var clock = new ManualClock(Backends.MidMinute);
        var shared = new CacheFake("shared", clock);
        await using var serverA = await TestApp.StartAsync(routes: Limited, services: Backends.Host(shared, clock));
        await using var serverB = await TestApp.StartAsync(routes: Limited, services: Backends.Host(shared, clock));

        (await serverA.Client.GetAsync("/limited")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await serverA.Client.GetAsync("/limited");
        rejected.StatusCode.Should().Be((HttpStatusCode)429);
        rejected.Headers.GetValues("Retry-After").Should().Equal("30");

        // The same caller is refused by the other instance: N instances no longer allow N times the limit.
        (await serverB.Client.GetAsync("/limited")).StatusCode.Should().Be((HttpStatusCode)429);

        await using var restarted = await TestApp.StartAsync(routes: Limited, services: Backends.Host(shared, clock));
        (await restarted.Client.GetAsync("/limited")).StatusCode.Should().Be((HttpStatusCode)429);

        clock.Advance(TimeSpan.FromSeconds(30));
        (await restarted.Client.GetAsync("/limited")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_headers_and_the_429_are_the_same_for_the_default_store_and_the_shared_store()
    {
        var clock = new ManualClock(Backends.MidMinute);
        await using var inProcess = await TestApp.StartAsync(routes: Limited2);
        await using var shared = await TestApp.StartAsync(routes: Limited2, services: Backends.Host(new CacheFake("shared", clock), clock));

        var expected = new[]
        {
            "200 limit=2 remaining=1 retry-after=none",
            "200 limit=2 remaining=0 retry-after=none",
            "429 limit=2 remaining=0 retry-after=present"
        };

        (await Describe(inProcess)).Should().Equal(expected);
        (await Describe(shared)).Should().Equal(expected);

        static async Task<string[]> Describe(TestApp app)
        {
            var lines = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var response = await app.Client.GetAsync("/limited");
                string Header(string name) => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "none";
                lines.Add($"{(int)response.StatusCode} limit={Header("X-RateLimit-Limit")} remaining={Header("X-RateLimit-Remaining")} retry-after={(Header("Retry-After") == "none" ? "none" : "present")}");
            }

            return lines.ToArray();
        }
    }

    [Fact]
    public async Task A_backend_outage_is_handled_by_the_policy_and_the_shared_quota_resumes_when_it_ends()
    {
        var clock = new ManualClock(Backends.MidMinute);
        var backend = new SwitchableStore(new CacheFake("shared", clock)) { Down = true };
        await using var app = await TestApp.StartAsync(
            routes: r =>
            {
                r.Get("/relaxed", () => "ok").Middleware("throttle:relaxed");
                r.Get("/strict", () => "ok").Middleware("throttle:strict");
            },
            options: o =>
            {
                o.ConfigureThrottlePolicy("relaxed", 1, TimeSpan.FromMinutes(1));
                o.ConfigureThrottlePolicy("strict", 1, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed);
            },
            services: Backends.Host(backend, clock));

        // Down: the default policy keeps this instance's own limit (never unlimited, never a 500) ...
        (await app.Client.GetAsync("/relaxed")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/relaxed")).StatusCode.Should().Be((HttpStatusCode)429);

        // ... and a policy that opted into FailClosed refuses with 503 and a Retry-After.
        var closed = await app.Client.GetAsync("/strict");
        closed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        long.Parse(closed.Headers.GetValues("Retry-After").Single()).Should().BeGreaterThan(0);

        // The backend is back and the breaker period has passed: one request probes it, and from then on the shared quota counts again.
        backend.Down = false;
        clock.Advance(TimeSpan.FromSeconds(6));
        (await app.Client.GetAsync("/relaxed")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/relaxed")).StatusCode.Should().Be((HttpStatusCode)429);
        backend.Calls.Should().Equal("increment", "increment");
    }
}
