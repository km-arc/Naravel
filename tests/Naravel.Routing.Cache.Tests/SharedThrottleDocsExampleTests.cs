using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Naravel.Routing.Tests;

namespace Naravel.Routing.Cache.Tests;

// Runs "Share one quota between instances" of docs/en/routing.md and docs/fa/routing.md. The guide's store is Redis
// (Driver "redis" and AddNaravelRedisCache); here the same store name is a Memory store (Driver "memory", which
// AddNaravelCache registers itself) so the example runs on every build. The Redis variant is covered by
// RedisSharedThrottleTests in the provider job. If a test here needs a change, the guide needs the same change.
public sealed class SharedThrottleDocsExampleTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Cache:Stores:shared:Driver"] = "memory",
        ["Cache:Stores:shared:Prefix"] = "myapp"
    }).Build();

    private static Task<TestApp> StartAsync(ManualClock clock) => TestApp.StartAsync(
        routes: r =>
        {
            r.Post("/login", (HttpContext c) => Results.Ok()).Middleware("throttle:login");
            r.Get("/api/books", () => "books").Middleware("throttle:api");
            r.Get("/inline", () => "ok").Middleware("throttle:1,1");
        },
        options: o =>
        {
            o.ConfigureThrottlePolicy("api", 120, TimeSpan.FromMinutes(1));
            o.ConfigureThrottlePolicy("login", 5, TimeSpan.FromMinutes(1), ThrottleFailureMode.FailClosed);
            o.Throttle.StoreTimeout = TimeSpan.FromMilliseconds(100);
            o.Throttle.BreakerPeriod = TimeSpan.FromSeconds(10);
        },
        services: services =>
        {
            services.AddSingleton<TimeProvider>(clock);
            services.AddNaravelCache(Configuration());
            services.AddNaravelSharedThrottle("shared");
        });

    [Fact]
    public async Task The_guides_policies_count_in_the_named_store_and_keep_their_headers()
    {
        var clock = new ManualClock(Backends.MidMinute);
        await using var app = await StartAsync(clock);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await app.Client.PostAsync("/login", content: null);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.GetValues("X-RateLimit-Limit").Should().Equal("5");
            response.Headers.GetValues("X-RateLimit-Remaining").Should().Equal((5 - attempt).ToString());
        }

        var rejected = await app.Client.PostAsync("/login", content: null);
        rejected.StatusCode.Should().Be((HttpStatusCode)429);
        rejected.Headers.GetValues("Retry-After").Should().Equal("30");

        (await app.Client.GetAsync("/api/books")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/inline")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.Client.GetAsync("/inline")).StatusCode.Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task The_guides_timing_options_are_the_ones_the_throttle_reads()
    {
        await using var app = await StartAsync(new ManualClock(Backends.MidMinute));

        var throttle = app.App.Services.GetRequiredService<IOptions<RoutingOptions>>().Value.Throttle;

        throttle.StoreTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        throttle.BreakerPeriod.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_memory_store_is_process_local_so_two_hosts_do_not_share_a_quota()
    {
        var clock = new ManualClock(Backends.MidMinute);
        await using var first = await StartAsync(clock);
        await using var second = await StartAsync(clock);

        (await first.Client.GetAsync("/inline")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Client.GetAsync("/inline")).StatusCode.Should().Be((HttpStatusCode)429);

        (await second.Client.GetAsync("/inline")).StatusCode.Should().Be(HttpStatusCode.OK, "the Memory store counts per process, exactly like the default store");
    }
}
