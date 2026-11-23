using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

// Refresh and force release of the Redis lock against a real server (CI "Provider integration tests" job; skipped unless NARAVEL_TEST_REDIS is set).
// The scenarios mirror the Memory lock tests in CacheLockTests, with larger time margins because every call is a network round trip.
public sealed class RedisLockCapabilityServerTests
{
    private static async Task WithLockAsync(Func<RedisLock, Task> scenario)
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        using var locks = new RedisLock(multiplexer, "naravel-test:" + Guid.NewGuid().ToString("N"), ownsConnection: false);
        await scenario(locks);
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Refresh_keeps_a_lock_past_its_first_lease_for_the_owner() => WithLockAsync(async locks =>
    {
        var token = await locks.AcquireAsync("job", TimeSpan.FromMilliseconds(1000));

        (await locks.RefreshAsync("job", token!, TimeSpan.FromSeconds(30))).Should().BeTrue();
        await Task.Delay(1400);

        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().BeNull("the refresh moved the end of the lease past the first one");
        (await locks.ReleaseAsync("job", token!)).Should().BeTrue();
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Refresh_by_a_caller_that_is_not_the_owner_changes_nothing() => WithLockAsync(async locks =>
    {
        await locks.AcquireAsync("job", TimeSpan.FromMilliseconds(300));

        (await locks.RefreshAsync("job", "not-owner", TimeSpan.FromSeconds(30))).Should().BeFalse();
        await Task.Delay(700);

        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull("the lease of the real owner ended on schedule");
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Refresh_sets_the_lease_to_the_new_value_and_does_not_add_to_it() => WithLockAsync(async locks =>
    {
        var token = await locks.AcquireAsync("job", TimeSpan.FromSeconds(30));

        (await locks.RefreshAsync("job", token!, TimeSpan.FromMilliseconds(300))).Should().BeTrue();
        await Task.Delay(700);

        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull("a lease of 30 seconds plus 300 milliseconds would still be held");
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task A_lease_that_ended_is_not_revived_even_when_nobody_took_the_lock() => WithLockAsync(async locks =>
    {
        var token = await locks.AcquireAsync("job", TimeSpan.FromMilliseconds(100));
        await Task.Delay(500);

        (await locks.RefreshAsync("job", token!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task A_holder_whose_lease_ended_cannot_refresh_or_release_the_lock_that_another_holder_took() => WithLockAsync(async locks =>
    {
        var first = await locks.AcquireAsync("job", TimeSpan.FromMilliseconds(100));
        await Task.Delay(500);
        var second = await locks.AcquireAsync("job", TimeSpan.FromSeconds(30));
        second.Should().NotBeNull();

        (await locks.RefreshAsync("job", first!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await locks.ReleaseAsync("job", first!)).Should().BeFalse();
        (await locks.RefreshAsync("job", second!, TimeSpan.FromSeconds(30))).Should().BeTrue();
        (await locks.ReleaseAsync("job", second!)).Should().BeTrue();
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task A_refresh_of_less_than_a_millisecond_is_rounded_up_and_does_not_make_the_lock_permanent() => WithLockAsync(async locks =>
    {
        var token = await locks.AcquireAsync("job", TimeSpan.FromSeconds(30));

        // PEXPIRE with zero would delete the key, and a fraction would be truncated to zero by a plain cast. The lease must end, not vanish early or last forever.
        (await locks.RefreshAsync("job", token!, TimeSpan.FromTicks(1))).Should().BeTrue();
        await Task.Delay(300);

        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().NotBeNull();
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Force_release_frees_the_name_and_the_previous_token_loses_it() => WithLockAsync(async locks =>
    {
        var first = await locks.AcquireAsync("job", TimeSpan.FromSeconds(30));

        (await locks.ForceReleaseAsync("job")).Should().BeTrue();
        var second = await locks.AcquireAsync("job", TimeSpan.FromSeconds(30));

        second.Should().NotBeNull();
        (await locks.ReleaseAsync("job", first!)).Should().BeFalse();
        (await locks.RefreshAsync("job", first!, TimeSpan.FromSeconds(30))).Should().BeFalse();
        (await locks.AcquireAsync("job", TimeSpan.FromSeconds(30))).Should().BeNull("the second holder still owns the lock");
        (await locks.ReleaseAsync("job", second!)).Should().BeTrue();
    });

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public Task Force_release_reports_false_when_there_is_no_lock_or_its_lease_ended() => WithLockAsync(async locks =>
    {
        (await locks.ForceReleaseAsync("never-held")).Should().BeFalse();

        await locks.AcquireAsync("expired", TimeSpan.FromMilliseconds(100));
        await Task.Delay(500);
        (await locks.ForceReleaseAsync("expired")).Should().BeFalse();
    });
}
