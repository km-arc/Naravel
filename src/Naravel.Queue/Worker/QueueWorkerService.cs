using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Collections.Concurrent;
using Naravel.Queue.Batching;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;
using Naravel.Queue.Diagnostics;
using Naravel.Queue.Events;
using Naravel.Queue.Failed;
using Naravel.Queue.Jobs;
using Naravel.Queue.Middleware;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Worker;

/// <summary>
/// Polls a connection's configured queues and executes jobs, handling retries with backoff,
/// permanent failure, chaining and batching. Register with services.AddQueueWorker(...).
/// You can register this hosted service multiple times (with different options) to run several
/// workers - e.g. one per connection, or one per priority queue - within the same process.
/// </summary>
public class QueueWorkerService : BackgroundService
{
    private readonly QueueWorkerOptions _options;
    private readonly QueueManager _manager;
    private readonly IJobSerializer _serializer;
    private readonly IJobDispatcher _dispatcher;
    private readonly IBatchRepository _batchRepository;
    private readonly IFailedJobStore _failedJobStore;
    private readonly IServiceProvider _rootProvider;
    private readonly ILogger<QueueWorkerService> _logger;
    private readonly ConcurrentDictionary<string, byte> _visibilityWarnings = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _startedAt;
    private int _jobsClaimed;

    public QueueWorkerService(
        QueueWorkerOptions options,
        QueueManager manager,
        IJobSerializer serializer,
        IJobDispatcher dispatcher,
        IBatchRepository batchRepository,
        IFailedJobStore failedJobStore,
        IServiceProvider rootProvider,
        ILogger<QueueWorkerService> logger)
    {
        _options = options;
        _manager = manager;
        _serializer = serializer;
        _dispatcher = dispatcher;
        _batchRepository = batchRepository;
        _failedJobStore = failedJobStore;
        _rootProvider = rootProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _startedAt = DateTimeOffset.UtcNow;
        try
        {
            await _batchRepository.ResumePendingCallbacksAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Naravel.Queue: could not resume pending batch callbacks during worker startup.");
        }

        var workers = Enumerable.Range(0, Math.Max(1, _options.Concurrency))
            .Select(_ => RunLoopAsync(stoppingToken));
        await Task.WhenAll(workers);
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (ShouldStopForControls()) break;

            // Resolved fresh every iteration (Manager caches internally, so this is cheap) rather than once
            // outside the loop, so a config reload that swaps this connection's driver (PDR-006 migration onto
            // Naravel.Foundation) takes effect without restarting the worker. The SAME instance is then used for
            // the whole pop→handle→ack/release/fail cycle of one message: RabbitMQ's delivery tags and Kafka's
            // consumer offsets are scoped to the specific driver/channel instance that popped the message.
            var driver = _manager.Connection(_options.Connection);
            WarnIfVisibilityTimeoutIsTooShort(driver, _options.JobTimeout, "$worker");
            QueuedMessage? message = null;

            foreach (var queue in _options.Queues)
            {
                try
                {
                    message = await driver.PopAsync(queue, stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Naravel.Queue: failed to pop from queue '{Queue}' on connection '{Connection}'.", queue, _options.Connection);
                    continue;
                }
                if (message != null) break;
            }

            if (message == null)
            {
                if (_options.StopWhenEmpty) break;
                try { await Task.Delay(_options.Rest, stoppingToken); } catch (OperationCanceledException) { }
                continue;
            }

            var stopAfterPop = IsRuntimeExpired() ||
                (_options.MaxJobs.HasValue && Volatile.Read(ref _jobsClaimed) >= _options.MaxJobs.Value);
            if (!stopAfterPop && _options.MaxJobs.HasValue &&
                Interlocked.Increment(ref _jobsClaimed) > _options.MaxJobs.Value)
            {
                Interlocked.Decrement(ref _jobsClaimed);
                stopAfterPop = true;
            }

            if (stopAfterPop)
            {
                try { await driver.ReleaseAsync(message, TimeSpan.Zero, stoppingToken); }
                catch (Exception releaseEx)
                {
                    _logger.LogError(releaseEx, "Naravel.Queue: could not release job {JobId} after worker controls stopped polling.", message.Id);
                }
                break;
            }

            try
            {
                await ProcessAsync(driver, message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // host is shutting down
            }
            catch (Exception ex)
            {
                // Never let one bad message (or a driver outage while acking/failing it) kill the worker loop -
                // an unhandled exception here would stop the whole host (BackgroundService default behaviour).
                _logger.LogError(ex, "Naravel.Queue: unexpected error while processing job {JobId}; the worker keeps running.", message.Id);
                try { await Task.Delay(_options.Rest, stoppingToken); } catch (OperationCanceledException) { }
            }
        }
    }

    private bool ShouldStopForControls()
        => (_options.MaxJobs.HasValue && Volatile.Read(ref _jobsClaimed) >= _options.MaxJobs.Value) ||
           IsRuntimeExpired();

    private bool IsRuntimeExpired()
        => _options.MaxRuntime.HasValue && DateTimeOffset.UtcNow - _startedAt >= _options.MaxRuntime.Value;

    private async Task ProcessAsync(IQueueDriver driver, QueuedMessage message, CancellationToken stoppingToken)
    {
        var parent = ActivityContext.TryParse(message.TraceParent, message.TraceState, out var parsedParent)
            ? parsedParent
            : default;
        using var activity = QueueTelemetry.ActivitySource.StartActivity("queue.process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.destination.name", message.Queue);
        activity?.SetTag("naravel.queue.connection", message.Connection);
        var startedAt = Stopwatch.GetTimestamp();
        try
        {
            await ProcessCoreAsync(driver, message, stoppingToken);
        }
        finally
        {
            QueueTelemetry.ProcessingDuration.Record(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

    private async Task ProcessCoreAsync(IQueueDriver driver, QueuedMessage message, CancellationToken stoppingToken)
    {
        if (!string.IsNullOrEmpty(message.BatchId) &&
            await _batchRepository.IsCancelledAsync(message.BatchId, stoppingToken))
        {
            await driver.AckAsync(message, stoppingToken);
            await _batchRepository.MarkJobCancelledAsync(message.BatchId, stoppingToken);
            return;
        }

        using var scope = _rootProvider.CreateScope();
        var sp = scope.ServiceProvider;
        var listeners = sp.GetServices<IQueueEventListener>().ToList();

        await NotifyAsync(listeners, l => l.OnProcessingAsync(message, stoppingToken));

        var context = new JobContext(sp, message);
        IJob? job = null;

        try
        {
            job = DeserializeJob(message.JobType, message.Payload);

            if (message.Attempts >= message.MaxAttempts)
            {
                var exhausted = new InvalidOperationException("The job exhausted its attempts while its reservation was reclaimed.");
                await HandleFailureAsync(driver, message, job, exhausted, sp, listeners, stoppingToken, attemptAlreadyCounted: true);
                return;
            }

            var perJobTimeout = (job as Job)?.Timeout;
            var effectiveTimeout = perJobTimeout.HasValue && _options.JobTimeout.HasValue
                ? (perJobTimeout.Value < _options.JobTimeout.Value ? perJobTimeout : _options.JobTimeout)
                : perJobTimeout ?? _options.JobTimeout;
            WarnIfVisibilityTimeoutIsTooShort(driver, effectiveTimeout, message.JobType);
            using var jobCts = effectiveTimeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
                : null;
            jobCts?.CancelAfter(effectiveTimeout!.Value);
            var jobToken = jobCts?.Token ?? stoppingToken;

            Func<Task> handler = () => job.HandleAsync(context, jobToken);
            foreach (var middleware in sp.GetServices<IJobMiddleware>().Reverse())
            {
                var next = handler;
                handler = () => middleware.InvokeAsync(context, next);
            }

            await handler();

            // At-least-once delivery: publish follow-up jobs BEFORE acknowledging, so a failure here retries this
            // job instead of silently losing the chain. Jobs must therefore be idempotent (documented in README).
            await ContinueChainAsync(message, context, stoppingToken);
            await driver.AckAsync(message, stoppingToken);
            QueueTelemetry.Processed.Add(1);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown interrupted the job: hand it back immediately WITHOUT counting an attempt, otherwise
            // drivers without visibility-timeout reclaim (memory/file/database) would keep it reserved forever.
            try { await driver.ReleaseAsync(message, TimeSpan.Zero, CancellationToken.None); }
            catch (Exception releaseEx)
            {
                _logger.LogError(releaseEx, "Naravel.Queue: could not release job {JobId} during shutdown.", message.Id);
            }
            throw;
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(driver, message, job, ex, sp, listeners, stoppingToken);
            return;
        }

        // The job is acknowledged from here on. Nothing below may route back into the failure/retry path,
        // or an already-completed job would be re-queued and executed twice.
        try
        {
            await NotifyAsync(listeners, l => l.OnProcessedAsync(message, stoppingToken));
            if (!string.IsNullOrEmpty(message.BatchId))
                await _batchRepository.MarkJobCompletedAsync(message.BatchId, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Naravel.Queue: job {JobId} completed, but post-completion bookkeeping failed.", message.Id);
        }
    }

    private async Task ContinueChainAsync(QueuedMessage message, JobContext context, CancellationToken stoppingToken)
    {
        // Runtime chaining: job called context.Then(nextJob) while executing.
        foreach (var next in context.ChainedJobs)
        {
            await _dispatcher.DispatchAsync(next, opts => ApplyChainOptions(opts, message), stoppingToken);
        }

        // Static chaining: dispatched via dispatcher.Chain(...).DispatchAsync().
        if (!string.IsNullOrEmpty(message.ChainedJobPayload))
        {
            var links = System.Text.Json.JsonSerializer.Deserialize<List<Naravel.Queue.Dispatch.ChainLink>>(message.ChainedJobPayload)!;
            var jobs = links.Select(l => DeserializeJob(l.JobType, l.Payload)).ToList();
            await _dispatcher.DispatchChainAsync(jobs, opts => ApplyChainOptions(opts, message), stoppingToken);
        }
    }

    private void ApplyChainOptions(DispatchOptions options, QueuedMessage message)
    {
        var connection = message.Connection ?? _options.Connection;
        if (!string.IsNullOrWhiteSpace(connection)) options.OnConnection(connection);
        options.OnQueue(message.Queue).WithPriority(message.Priority).WithMaxAttempts(message.MaxAttempts);
    }

    private IJob DeserializeJob(string alias, string payload)
    {
        var type = _serializer.ResolveType(alias);
        if (!typeof(IJob).IsAssignableFrom(type))
            throw new UnknownJobTypeException(alias);

        return _serializer.Deserialize(payload, type) as IJob
            ?? throw new InvalidDataException($"Queue payload for alias '{alias}' did not deserialize to an IJob.");
    }

    private async Task HandleFailureAsync(
        IQueueDriver driver,
        QueuedMessage message,
        IJob? job,
        Exception ex,
        IServiceProvider sp,
        List<IQueueEventListener> listeners,
        CancellationToken stoppingToken,
        bool attemptAlreadyCounted = false)
    {
        if (!attemptAlreadyCounted) message.Attempts++;
        message.Error = ex.ToString();

        if (message.Attempts >= message.MaxAttempts)
        {
            message.Connection ??= _options.Connection;
            await _failedJobStore.RecordAsync(message, ex, stoppingToken);
            await driver.FailAsync(message, stoppingToken);
            QueueTelemetry.Failed.Add(1);
            _logger.LogError(ex, "Naravel.Queue: job {JobId} on queue '{Queue}' failed permanently after {Attempts} attempts.",
                message.Id, message.Queue, message.Attempts);

            try
            {
                if (job is IFailedJobHandler failable)
                    await failable.FailedAsync(new JobContext(sp, message), ex, stoppingToken);
            }
            catch (Exception handlerEx)
            {
                _logger.LogError(handlerEx, "Naravel.Queue: error while invoking FailedAsync for job {JobId}.", message.Id);
            }

            await NotifyAsync(listeners, l => l.OnFailedAsync(message, ex, stoppingToken));

            if (!string.IsNullOrEmpty(message.BatchId))
                await _batchRepository.MarkJobFailedAsync(message.BatchId, ex, stoppingToken);
        }
        else
        {
            var delay = ComputeBackoff(message, job);
            await driver.ReleaseAsync(message, delay, stoppingToken);
            QueueTelemetry.Retried.Add(1);
            _logger.LogWarning(ex, "Naravel.Queue: job {JobId} on queue '{Queue}' failed (attempt {Attempt}/{Max}), retrying in {Delay}.",
                message.Id, message.Queue, message.Attempts, message.MaxAttempts, delay);
            await NotifyAsync(listeners, l => l.OnRetryingAsync(message, ex, delay, stoppingToken));
        }
    }

    private void WarnIfVisibilityTimeoutIsTooShort(IQueueDriver driver, TimeSpan? jobTimeout, string scope)
    {
        var visibilityTimeout = driver.VisibilityTimeout;
        if (!visibilityTimeout.HasValue || !jobTimeout.HasValue ||
            visibilityTimeout.Value >= jobTimeout.Value + _options.VisibilityTimeoutMargin)
            return;

        var connection = _options.Connection ?? _manager.DefaultConnectionName;
        if (!_visibilityWarnings.TryAdd($"{connection}:{scope}", 0)) return;

        _logger.LogWarning(
            "Naravel.Queue: visibility timeout {VisibilityTimeout} on connection '{Connection}' is shorter than job timeout {JobTimeout} plus margin {Margin}; the job may be reclaimed while running.",
            visibilityTimeout.Value, connection, jobTimeout.Value, _options.VisibilityTimeoutMargin);
    }

    private static TimeSpan ComputeBackoff(QueuedMessage message, IJob? job)
    {
        if (job is Job baseJob && baseJob.Backoff.Length > 0)
        {
            var index = Math.Min(message.Attempts - 1, baseJob.Backoff.Length - 1);
            return baseJob.Backoff[index];
        }
        return TimeSpan.FromSeconds(Math.Min(60, 5 * message.Attempts));
    }

    private async Task NotifyAsync(List<IQueueEventListener> listeners, Func<IQueueEventListener, Task> call)
    {
        foreach (var listener in listeners)
        {
            try { await call(listener); }
            catch (Exception ex) { _logger.LogError(ex, "Naravel.Queue: event listener {Listener} threw an exception.", listener.GetType().Name); }
        }
    }
}
