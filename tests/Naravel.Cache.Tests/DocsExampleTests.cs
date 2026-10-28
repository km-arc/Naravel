using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Abstractions;
using Naravel.Cache.RateLimiting;
using Naravel.Cache.Tagging;

namespace Naravel.Cache.Tests;

// Runs the snippets of docs/en/cache.md and docs/fa/cache.md against the Memory store and lock.
// If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    private static ServiceProvider Build()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memory",
            ["Cache:AppPrefix"] = "orders-api",
            ["Cache:Stores:memory:Driver"] = "memory",
            ["Cache:Stores:memory:Prefix"] = "orders-api:memory",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration);
        services.AddNaravelRateLimiter("memory");
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Basic_operations_and_RememberAsync()
    {
        await using var provider = Build();
        var cache = provider.GetRequiredService<ICacheStore>();

        await cache.SetAsync("k", "v", TimeSpan.FromMinutes(10));
        var (found, value) = await cache.TryGetAsync<string>("k");
        found.Should().BeTrue();
        value.Should().Be("v");

        var loads = 0;
        Func<CancellationToken, Task<string>> load = _ => { loads++; return Task.FromResult("settings"); };
        await cache.RememberAsync("site-settings", TimeSpan.FromMinutes(10), load);
        await cache.RememberAsync("site-settings", TimeSpan.FromMinutes(10), load);
        loads.Should().Be(1);

        (await cache.PullAsync<string>("k")).Should().Be("v");
        (await cache.ExistsAsync("k")).Should().BeFalse();
    }

    [Fact]
    public async Task Scopes_isolate_users_and_flushing_a_tag_invalidates_its_entries()
    {
        await using var provider = Build();
        var manager = provider.GetRequiredService<CacheManager>();
        var ttl = TimeSpan.FromMinutes(5);

        await manager.ForUser(7).SetAsync("cart-count", 3, ttl);
        (await manager.ForUser(7).TryGetAsync<int>("cart-count")).Found.Should().BeTrue();
        (await manager.ForUser(8).TryGetAsync<int>("cart-count")).Found.Should().BeFalse();

        var tenant = manager.Scope("tenant:t1");
        await tenant.Tags("orders", "recent").SetAsync("last-order", "o1", ttl);
        (await tenant.Tags("orders", "recent").TryGetAsync<string>("last-order")).Found.Should().BeTrue();

        await tenant.Tags("orders").FlushAsync();
        (await tenant.Tags("orders", "recent").TryGetAsync<string>("last-order")).Found.Should().BeFalse();
    }

    [Fact]
    public async Task Memory_lock_runs_the_callback_and_only_the_owner_token_releases()
    {
        await using var provider = Build();
        var cacheLock = provider.GetRequiredService<LockManager>().Driver("memory");

        var ran = false;
        var acquired = await cacheLock.BlockAsync(
            "process-order:1",
            ttl: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            callback: async _ => { ran = true; await Task.Yield(); });
        acquired.Should().BeTrue();
        ran.Should().BeTrue();

        var token = await cacheLock.AcquireAsync("held", TimeSpan.FromSeconds(30));
        token.Should().NotBeNull();
        (await cacheLock.AcquireAsync("held", TimeSpan.FromSeconds(30))).Should().BeNull();
        (await cacheLock.ReleaseAsync("held", "not-the-token")).Should().BeFalse();
        (await cacheLock.ReleaseAsync("held", token!)).Should().BeTrue();
    }

    [Fact]
    public async Task Login_throttle_allows_five_then_rejects_and_ClearAsync_resets_the_bucket()
    {
        await using var provider = Build();
        var limiter = provider.GetRequiredService<IRateLimiter>();
        var window = TimeSpan.FromMinutes(1);
        const string subject = "1.2.3.4:user@example.com";

        var decisions = new List<RateLimitDecision>();
        for (var i = 0; i < 7; i++) decisions.Add(await limiter.AttemptAsync("login", subject, 5, window));

        decisions.Take(5).Should().OnlyContain(d => d.Allowed);
        decisions.Skip(5).Should().OnlyContain(d => !d.Allowed);
        decisions.Select(d => d.Remaining).Should().Equal(4, 3, 2, 1, 0, 0, 0);
        decisions[5].RetryAfter.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(window);

        var otherSubject = await limiter.AttemptAsync("login", "someone-else", 5, window);
        otherSubject.Allowed.Should().BeTrue();

        await limiter.ClearAsync("login", subject);
        (await limiter.AttemptAsync("login", subject, 5, window)).Allowed.Should().BeTrue();
    }
}
