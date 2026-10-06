using Naravel.Queue.Testing;

namespace Naravel.Queue.Tests;

public class QueueFakeTests
{
    [Fact]
    public async Task Fake_asserts_dispatched_and_chained_jobs()
    {
        var fake = new QueueFake();
        var head = new RecordingJob { RunId = "head" };
        var followup = new RecordingJob { RunId = "followup" };

        await fake.DispatchAsync(head);
        await fake.Chain(head, followup).DispatchAsync();

        fake.AssertDispatched<RecordingJob>().Should().BeSameAs(head);
        fake.AssertChained<RecordingJob>().Should().ContainSingle().Which.Should().BeSameAs(followup);
    }
}