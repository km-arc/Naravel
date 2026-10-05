using Naravel.Queue.Kafka;

namespace Naravel.Queue.Providers.Tests;

public sealed class KafkaOffsetCommitTrackerTests
{
    [Fact]
    public void Later_ack_does_not_advance_past_an_unacknowledged_record()
    {
        var tracker = new KafkaOffsetCommitTracker();
        tracker.RecordDelivered(12);
        tracker.RecordDelivered(13);

        tracker.MarkAcknowledged(13).Should().BeNull();
        tracker.MarkAcknowledged(12).Should().Be(14);
    }

    [Fact]
    public void Commit_candidates_follow_delivery_order_when_offsets_have_gaps()
    {
        var tracker = new KafkaOffsetCommitTracker();
        tracker.RecordDelivered(3);
        tracker.RecordDelivered(8);
        tracker.MarkAcknowledged(8).Should().BeNull();
        tracker.MarkAcknowledged(3).Should().Be(9);
    }
}