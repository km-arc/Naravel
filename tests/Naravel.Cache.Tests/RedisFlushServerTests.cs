using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

// The store and the lock of a Redis cache share one prefix, so clearing the cache must not release a lock that is held.
// Runs against a real server (CI "Provider integration tests" job); skipped unless NARAVEL_TEST_REDIS is set.
public sealed class RedisFlushServerTests
{
    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_flush_removes_the_cache_keys_and_never_releases_a_held_lock()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var prefix = "naravel-test:" + Guid.NewGuid().ToString("N");
        using var store = new RedisCacheStore("flush", multiplexer, prefix, ownsConnection: false);
        using var locks = new RedisLock(multiplexer, prefix, ownsConnection: false);
        await Task.WhenAll(Enumerable.Range(0, 700).Select(i => store.SetAsync($"key-{i}", i, null)));
        var token = await locks.AcquireAsync("job", TimeSpan.FromMinutes(1));
        token.Should().NotBeNull();

        await store.FlushAsync();

        (await store.ExistsAsync("key-0")).Should().BeFalse();
        (await store.ExistsAsync("key-699")).Should().BeFalse("every batch of deletes must run");
        (await locks.AcquireAsync("job", TimeSpan.FromMinutes(1))).Should().BeNull("clearing the cache must not release a lock that is held");
        (await locks.ReleaseAsync("job", token!)).Should().BeTrue();
    }
}
