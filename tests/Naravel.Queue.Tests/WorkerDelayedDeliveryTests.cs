using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;

namespace Naravel.Queue.Tests;

/// <summary>
/// PDR-012a: when a driver cannot delay one message without blocking the others (Kafka), the worker releases a failed
/// job immediately and says so once per job type instead of silently ignoring the job's backoff.
/// </summary>
public class WorkerDelayedDeliveryTests
{
    private sealed class NoDelayDriver : IQueueDriver
    {
        private readonly object _gate = new();
        private readonly List<QueuedMessage> _messages = new();

        public List<TimeSpan> ReleaseDelays { get; } = new();

        public bool SupportsDelayedDelivery => false;

        public Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default)
        {
            lock (_gate) _messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var next = _messages.FirstOrDefault(m => m.Queue == queue && m.AvailableAt <= DateTimeOffset.UtcNow);
                if (next is not null) _messages.Remove(next);
                return Task.FromResult<QueuedMessage?>(next);
            }
        }

        public Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ReleaseDelays.Add(delay);
                message.AvailableAt = DateTimeOffset.UtcNow;
                _messages.Add(message);
            }
            return Task.CompletedTask;
        }

        public Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default)
        {
            lock (_gate) return Task.FromResult((long)_messages.Count);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries) entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    [Fact]
    public async Task A_driver_without_delayed_delivery_gets_immediate_retries_and_one_warning_per_job_type()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["NaravelQueue:Default"] = "custom",
        }).Build();
        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(logs));
        services.AddQueue(config);
        services.AddQueueWorker(o =>
        {
            o.Connection = "custom";
            o.Queues = new[] { "q" };
            o.SleepWhenEmpty = TimeSpan.FromMilliseconds(15);
        });
        await using var provider = services.BuildServiceProvider();
        var driver = new NoDelayDriver();
        provider.GetRequiredService<QueueManager>().Extend("custom", _ => driver);
        var worker = provider.GetServices<IHostedService>().Single();

        var id = Guid.NewGuid().ToString("N");
        // FlakyJob declares a 10 ms backoff and fails three times, so it is released three times.
        await provider.GetRequiredService<IJobDispatcher>().DispatchAsync(new FlakyJob { RunId = id, FailTimes = 3 }, o => o.OnQueue("q"));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            (await Wait.Until(() => Recorder.Count(id, "ok") == 1)).Should().BeTrue();
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        driver.ReleaseDelays.Should().HaveCount(3).And.OnlyContain(delay => delay == TimeSpan.Zero);
        lock (logs.Entries)
        {
            logs.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("does not support delayed delivery"))
                .Should().Be(1, "the warning is logged once per job type, not once per retry");
        }
    }
}
