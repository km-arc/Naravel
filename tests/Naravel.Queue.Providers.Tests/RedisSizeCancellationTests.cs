using Naravel.Queue.Redis;

namespace Naravel.Queue.Providers.Tests;

// RQR-002 (the part that owner decision AD-020 does not cover): SizeAsync used to ignore its token and send eleven requests even for a token that
// was already cancelled, while every other method of the driver checks the token first. The test needs no Redis server: the driver is built
// without a connection, so any attempt to send a request would fail with a NullReferenceException instead of the exception asserted here.
//
// Not covered here, because it needs a server and cannot be timed deterministically: cancellation between two of the eleven requests, and a token
// that is cancelled while a request is in flight (the latter is what AD-020 is about and is not decided). That a token which is not cancelled
// still returns the count is covered by the provider contract tests that call SizeAsync against a live server.
public sealed class RedisSizeCancellationTests
{
    [Fact]
    public async Task A_token_that_is_already_cancelled_throws_before_any_request_is_made()
    {
        var driver = new RedisQueueDriver(null!, TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await driver.SizeAsync("jobs", cts.Token);

        // OperationCanceledException (and not NullReferenceException) shows that the token was checked before the connection was used.
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.CancellationToken.Should().Be(cts.Token);
    }
}
