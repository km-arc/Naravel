using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Naravel.Queue.Tests;

[CollectionDefinition("Queue telemetry", DisableParallelization = true)]
public sealed class QueueTelemetryCollection;

[Collection("Queue telemetry")]
public class QueueTelemetryTests
{
    [Fact]
    public async Task Meter_reports_processed_failed_retried_and_processing_duration()
    {
        var measurements = new ConcurrentDictionary<string, long>();
        var durationMeasurements = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Naravel.Queue")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.AddOrUpdate(instrument.Name, value, (_, current) => current + value));
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            if (instrument.Name == "naravel.queue.processing.duration")
                Interlocked.Increment(ref durationMeasurements);
        });
        listener.Start();

        await using var host = new QueueTestHost();
        var completedId = Guid.NewGuid().ToString("N");
        var failedId = Guid.NewGuid().ToString("N");
        await host.Dispatch(new RecordingJob { RunId = completedId });
        await host.Dispatch(new AlwaysFailingJob { RunId = failedId });

        (await Wait.Until(() => Recorder.Count(completedId, "run") == 1 && Recorder.Count(failedId, "failed") == 1)).Should().BeTrue();
        measurements["naravel.queue.processed"].Should().Be(1);
        measurements["naravel.queue.failed"].Should().Be(1);
        measurements["naravel.queue.retried"].Should().Be(1);
        durationMeasurements.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task W3c_trace_context_is_restored_from_the_queued_envelope()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Naravel.Queue",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        await using var host = new QueueTestHost();
        var id = Guid.NewGuid().ToString("N");
        string traceId;
        using (var producer = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            producer!.TraceStateString = "vendor=naravel";
            traceId = producer.TraceId.ToString();
            await host.Dispatch(new TraceRecordingJob { RunId = id });
        }

        (await Wait.Until(() => Recorder.Count(id, $"{traceId}|vendor=naravel") == 1)).Should().BeTrue();
    }
}