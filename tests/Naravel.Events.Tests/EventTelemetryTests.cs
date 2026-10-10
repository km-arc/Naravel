using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Events;

namespace Naravel.Events.Tests;

[CollectionDefinition("Event telemetry", DisableParallelization = true)]
public sealed class EventTelemetryCollection;

[Collection("Event telemetry")]
public sealed class EventTelemetryTests
{
    [Fact]
    public async Task Dispatch_emits_meter_measurements_and_activity()
    {
        var measurements = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        var durationCount = 0;
        var activities = 0;
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "Naravel.Events")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.AddOrUpdate(instrument.Name, value, (_, current) => current + value));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, _, _) =>
        {
            if (instrument.Name == "naravel.events.dispatch.duration")
            {
                Interlocked.Increment(ref durationCount);
            }
        });
        meterListener.Start();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Naravel.Events",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _ => Interlocked.Increment(ref activities)
        };
        ActivitySource.AddActivityListener(activityListener);

        var services = new ServiceCollection();
        services.AddNaravelEvents();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
            .DispatchAsync(new OrderPaid("telemetry"));

        measurements["naravel.events.dispatched"].Should().Be(1);
        durationCount.Should().Be(1);
        activities.Should().Be(1);
    }

    [Fact]
    public async Task Failed_dispatch_emits_failure_measurement()
    {
        var failures = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Naravel.Events" &&
                    instrument.Name == "naravel.events.failed")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref failures, (int)value));
        listener.Start();

        var services = new ServiceCollection();
        services.AddNaravelEvents();
        services.AddSingleton<IEventListener<OrderPaid>, FailingListener>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        await FluentActions.Invoking(() => scope.ServiceProvider.GetRequiredService<IEventDispatcher>()
            .DispatchAsync(new OrderPaid("failure"))).Should().ThrowAsync<InvalidOperationException>();

        failures.Should().Be(1);
    }
}
