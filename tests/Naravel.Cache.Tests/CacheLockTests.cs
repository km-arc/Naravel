using Naravel.Cache.Locks;

namespace Naravel.Cache.Tests;

public sealed class CacheLockTests
{
    [Fact]
    public async Task Only_the_current_token_can_release_a_lock()
    {
        var cacheLock = new MemoryLock("tests");
        var token = await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5));

        token.Should().NotBeNull();
        (await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5))).Should().BeNull();
        (await cacheLock.ReleaseAsync("order:1", "not-owner")).Should().BeFalse();
        (await cacheLock.ReleaseAsync("order:1", token!)).Should().BeTrue();
        (await cacheLock.AcquireAsync("order:1", TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task Block_async_runs_and_releases_the_lock_even_when_callback_fails()
    {
        var cacheLock = new MemoryLock("tests");
        var act = () => cacheLock.BlockAsync("failing", TimeSpan.FromSeconds(5), TimeSpan.Zero,
            _ => throw new InvalidOperationException("expected"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await cacheLock.AcquireAsync("failing", TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }
}
