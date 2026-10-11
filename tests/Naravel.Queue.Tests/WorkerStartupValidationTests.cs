using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Naravel.Queue.Drivers;
using Naravel.Queue.Extensions;
using Naravel.Queue.Worker;

namespace Naravel.Queue.Tests;

/// <summary>
/// R07.T03 / D-13: the worker validates the driver's visibility timeout against its own job timeouts at startup.
/// Visibility below the worker default <c>JobTimeout</c> is a configuration error (the job is reclaimed while it is
/// still allowed to run); <c>MaxJobTimeout</c> is an optional upper bound that only produces a warning.
/// </summary>
public class WorkerStartupValidationTests
{
    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);
        public void Dispose() { }

        private sealed class CapturingLogger(List<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries) entries.Add(new LogEntry(logLevel, formatter(state, exception)));
            }
        }
    }

    private sealed class VisibilityDriver(TimeSpan? visibilityTimeout) : IQueueDriver
    {
        public TimeSpan? VisibilityTimeout => visibilityTimeout;
        public Task PushAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<QueuedMessage?> PopAsync(string queue, CancellationToken ct = default) => Task.FromResult<QueuedMessage?>(null);
        public Task AckAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken ct = default) => Task.CompletedTask;
        public Task FailAsync(QueuedMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> SizeAsync(string queue, CancellationToken ct = default) => Task.FromResult(0L);
    }

    private static (IHostedService Worker, CapturingLoggerProvider Logs, ServiceProvider Services) Build(
        TimeSpan? visibilityTimeout, Action<QueueWorkerOptions> configureWorker)
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
            o.StopWhenEmpty = true;
            o.Rest = TimeSpan.FromMilliseconds(5);
            configureWorker(o);
        });
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<QueueManager>().Extend("custom", _ => new VisibilityDriver(visibilityTimeout));
        return (provider.GetServices<IHostedService>().Single(), logs, provider);
    }

    private static async Task RunToCompletionAsync(IHostedService worker)
    {
        await worker.StartAsync(CancellationToken.None);
        if (worker is BackgroundService { ExecuteTask: { } task }) await task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_fails_with_an_error_when_visibility_timeout_is_below_the_worker_job_timeout()
    {
        var (worker, logs, services) = Build(TimeSpan.FromSeconds(30), o => o.JobTimeout = TimeSpan.FromSeconds(60));
        using var provider = services;

        var act = () => worker.StartAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("visibility timeout").And.Contain("custom");
        logs.Entries.Should().Contain(e => e.Level == LogLevel.Error && e.Message.Contains("visibility timeout"));
    }

    [Fact]
    public async Task Startup_succeeds_without_warning_when_visibility_timeout_equals_the_worker_job_timeout()
    {
        var (worker, logs, services) = Build(TimeSpan.FromSeconds(60), o => o.JobTimeout = TimeSpan.FromSeconds(60));
        using var provider = services;

        await RunToCompletionAsync(worker);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("visibility timeout"));
    }

    [Fact]
    public async Task Startup_warns_when_visibility_timeout_is_below_MaxJobTimeout_plus_margin()
    {
        var (worker, logs, services) = Build(TimeSpan.FromSeconds(60), o =>
        {
            o.JobTimeout = TimeSpan.FromSeconds(30);
            o.MaxJobTimeout = TimeSpan.FromSeconds(60);
            o.VisibilityMargin = TimeSpan.FromSeconds(10);
        });
        using var provider = services;

        await RunToCompletionAsync(worker);

        logs.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("MaxJobTimeout"));
        logs.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Startup_does_not_warn_when_visibility_timeout_covers_MaxJobTimeout_plus_margin()
    {
        var (worker, logs, services) = Build(TimeSpan.FromSeconds(70), o =>
        {
            o.MaxJobTimeout = TimeSpan.FromSeconds(60);
            o.VisibilityMargin = TimeSpan.FromSeconds(10);
        });
        using var provider = services;

        await RunToCompletionAsync(worker);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("visibility timeout"));
    }

    [Fact]
    public async Task No_max_timeout_warning_is_emitted_when_MaxJobTimeout_is_unset()
    {
        // Visibility (20 s) is far below the default margin plus any imaginable job, but nothing was declared.
        var (worker, logs, services) = Build(TimeSpan.FromSeconds(20), _ => { });
        using var provider = services;

        await RunToCompletionAsync(worker);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("visibility timeout"));
    }

    [Fact]
    public async Task Drivers_without_a_visibility_timeout_are_never_validated()
    {
        var (worker, logs, services) = Build(null, o =>
        {
            o.JobTimeout = TimeSpan.FromHours(1);
            o.MaxJobTimeout = TimeSpan.FromHours(2);
        });
        using var provider = services;

        await RunToCompletionAsync(worker);

        logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning && e.Message.Contains("visibility timeout"));
    }
}
