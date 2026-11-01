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

    /// <summary>Initializes a new instance of the <see cref="QueueWorkerService"/> class with its options and the queue, serialization, dispatch, batch and failed-job services it needs.</summary>
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

    /// <summary>
    /// Validates the driver's visibility timeout before the worker starts. Done here rather than in
    /// <see cref="BackgroundService.ExecuteAsync"/> because whether an exception thrown from ExecuteAsync reaches
    /// <c>StartAsync</c> depends on the runtime's BackgroundService implementation; throwing from StartAsync itself
    /// reliably stops the host before any job is reserved.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        ValidateVisibilityTimeoutAtStartup();
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Resumes pending batch callbacks, then runs <see cref="QueueWorkerOptions.Concurrency"/> polling loops until the host stops or a worker limit is reached.</summary>
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
                // CancellationToken.None, not stoppingToken: this branch is reached exactly when the worker is winding down, and a
                // driver that honours its token (EF Core, a network client) would throw here and leave the message reserved.
                try { await driver.ReleaseAsync(message, TimeSpan.Zero, CancellationToken.None); }
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
                // ProcessCoreAsync has already handed the message back if it was still reserved.
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

    /// <summary>
    /// Runs one reserved message and guarantees it does not stay reserved. Every exit of <see cref="RunReservedAsync"/> either
    /// settles the message (acknowledge, release or fail) or throws; when it throws with the message still unsettled, the
    /// message is handed back here before the exception continues to the polling loop. Without this, a driver that has no
    /// expiry-based reclaim (Memory, RabbitMQ, Kafka) would keep the message reserved until the process ends, and the
    /// others until the visibility timeout.
    /// </summary>
    private async Task ProcessCoreAsync(IQueueDriver driver, QueuedMessage message, CancellationToken stoppingToken)
    {
        var reservation = new Reservation();
        try
        {
            await RunReservedAsync(driver, message, reservation, stoppingToken);
        }
        catch (Exception ex) when (!reservation.Settled)
        {
            await HandBackAsync(driver, message, ex);
            throw;
        }
    }

    private async Task RunReservedAsync(IQueueDriver driver, QueuedMessage message, Reservation reservation, CancellationToken stoppingToken)
    {
        IServiceScope? scope = null;
        var sp = _rootProvider;
        var listeners = new List<IQueueEventListener>();
        IJob? job = null;

        try
        {
            try
            {
                // Every step from here to the job handler is inside this try on purpose: the batch store, the DI container
                // (listeners, middleware, the job's own dependencies) and the serializer can all throw before the job runs,
                // and each of those must take the same path as a job that threw - count the attempt, retry with backoff,
                // and finally record the failure - instead of leaving the reserved message behind.
                if (!string.IsNullOrEmpty(message.BatchId) &&
                    await _batchRepository.IsCancelledAsync(message.BatchId, stoppingToken))
                {
                    await driver.AckAsync(message, stoppingToken);
                    reservation.Settle();
                    await _batchRepository.MarkJobCancelledAsync(message.BatchId, stoppingToken);
                    return;
                }

                scope = _rootProvider.CreateScope();
                sp = scope.ServiceProvider;
                listeners = sp.GetServices<IQueueEventListener>().ToList();

                await NotifyAsync(listeners, l => l.OnProcessingAsync(message, stoppingToken));

                var context = new JobContext(sp, message);
                var runnable = DeserializeJob(message.JobType, message.Payload);
                job = runnable;

                if (message.Attempts >= message.MaxAttempts)
                {
                    var exhausted = new InvalidOperationException("The job exhausted its attempts while its reservation was reclaimed.");
                    await HandleFailureAsync(driver, message, runnable, exhausted, sp, listeners, reservation, stoppingToken, attemptAlreadyCounted: true);
                    return;
                }

                var perJobTimeout = (runnable as Job)?.Timeout;
                var effectiveTimeout = perJobTimeout.HasValue && _options.JobTimeout.HasValue
                    ? (perJobTimeout.Value < _options.JobTimeout.Value ? perJobTimeout : _options.JobTimeout)
                    : perJobTimeout ?? _options.JobTimeout;
                WarnIfVisibilityTimeoutIsTooShort(driver, effectiveTimeout, message.JobType);
                using var jobCts = effectiveTimeout.HasValue
                    ? CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)
                    : null;
                jobCts?.CancelAfter(effectiveTimeout!.Value);
                var jobToken = jobCts?.Token ?? stoppingToken;

                Func<Task> handler = async () =>
                {
                    context.JobExecuted = true;
                    if (runnable is IUniqueJob { UniqueUntilProcessing: true })
                        await ReleaseUniqueAsync(runnable, message);
                    await runnable.HandleAsync(context, jobToken);
                };
                var middlewares = (runnable is IHasJobMiddleware own ? own.Middleware(context) : Enumerable.Empty<IJobMiddleware>())
                    .Reverse()
                    .Concat(sp.GetServices<IJobMiddleware>().Reverse());
                foreach (var middleware in middlewares)
                {
                    var next = handler;
                    handler = () => middleware.InvokeAsync(context, next);
                }

                await handler();

                if (!context.JobExecuted)
                {
                    if (context.Outcome == JobOutcome.Released)
                    {
                        var delay = context.ReleaseDelay > TimeSpan.Zero && !driver.SupportsDelayedDelivery ? TimeSpan.Zero : context.ReleaseDelay;
                        await driver.ReleaseAsync(message, delay, stoppingToken);
                        reservation.Settle();
                        QueueTelemetry.Outcome.Add(1, new KeyValuePair<string, object?>("outcome", "released"));
                        return;
                    }
                    if (context.Outcome == JobOutcome.Skipped)
                    {
                        await driver.AckAsync(message, stoppingToken);
                        reservation.Settle();
                        await ReleaseUniqueAsync(runnable, message);
                        QueueTelemetry.Outcome.Add(1, new KeyValuePair<string, object?>("outcome", "skipped"));
                        if (!string.IsNullOrEmpty(message.BatchId))
                            await _batchRepository.MarkJobCancelledAsync(message.BatchId, stoppingToken);
                        return;
                    }
                    throw new InvalidOperationException(
                        "A job middleware returned without calling next and without calling JobContext.Release or JobContext.Skip, so the job was not run. Call one of them, or call next.");
                }

                // At-least-once delivery: publish follow-up jobs BEFORE acknowledging, so a failure here retries this
                // job instead of silently losing the chain. Jobs must therefore be idempotent (documented in README).
                await ContinueChainAsync(message, context, stoppingToken);
                await driver.AckAsync(message, stoppingToken);
                reservation.Settle();
                await ReleaseUniqueAsync(runnable, message);
                QueueTelemetry.Processed.Add(1);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown interrupted the message: ProcessCoreAsync hands it back immediately WITHOUT counting an
                // attempt (unless it was already settled), otherwise drivers without reclaim (Memory, RabbitMQ, Kafka)
                // would keep it reserved for the life of the process.
                throw;
            }
            catch (Exception ex) when (!reservation.FailureHandlingStarted)
            {
                if (reservation.Settled)
                {
                    // Acknowledged, released or failed already: the message belongs to nobody now. Routing this through the
                    // failure path would put a finished (for example skipped or batch-cancelled) job back on the queue.
                    _logger.LogError(ex, "Naravel.Queue: job {JobId} was settled, but the bookkeeping after it failed.", message.Id);
                    return;
                }

                await HandleFailureAsync(driver, message, job, ex, sp, listeners, reservation, stoppingToken);
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
        finally
        {
            scope?.Dispose();
        }
    }

    /// <summary>
    /// Best-effort release of a message whose processing threw before it was acknowledged, released or failed. It uses
    /// <see cref="CancellationToken.None"/> so a stopping host cannot cancel it, adds no attempt (a failure the worker
    /// counted was counted before this runs), and on a shutdown hands the message back immediately. Any other cause waits
    /// one <see cref="QueueWorkerOptions.Rest"/> on drivers that can delay, so a broken store is not hit in a hot loop.
    /// A fenced driver (Redis, File, Database) ignores the call when the reservation already belongs to another worker.
    /// </summary>
    private async Task HandBackAsync(IQueueDriver driver, QueuedMessage message, Exception cause)
    {
        var shuttingDown = cause is OperationCanceledException;
        var delay = shuttingDown || !driver.SupportsDelayedDelivery ? TimeSpan.Zero : _options.Rest;
        try
        {
            await driver.ReleaseAsync(message, delay, CancellationToken.None);
            if (!shuttingDown)
            {
                _logger.LogWarning(cause,
                    "Naravel.Queue: processing of job {JobId} on queue '{Queue}' stopped before the message was acknowledged, released or failed; it was handed back to the queue.",
                    message.Id, message.Queue);
            }
        }
        catch (Exception releaseEx)
        {
            _logger.LogError(releaseEx,
                "Naravel.Queue: could not hand job {JobId} on queue '{Queue}' back to the queue; it stays reserved until the driver reclaims it (visibility timeout) or the worker restarts.",
                message.Id, message.Queue);
        }
    }

    /// <summary>What happened to one reserved message while this worker holds it.</summary>
    private sealed class Reservation
    {
        /// <summary>True once the message was acknowledged, released or failed (or the driver reported the reservation is no longer ours).</summary>
        public bool Settled { get; private set; }

        /// <summary>True once <c>HandleFailureAsync</c> started, so a failure inside it is never handled a second time (the attempt would be counted twice).</summary>
        public bool FailureHandlingStarted { get; set; }

        public void Settle() => Settled = true;
    }

    /// <summary>
    /// Frees the <see cref="IUniqueJob"/> key of a finished job. Never throws: it runs on the success path after the
    /// message was acknowledged, where an exception would re-queue a completed job. A failure here only leaves the key
    /// locked until its TTL, which is logged.
    /// </summary>
    private async Task ReleaseUniqueAsync(IJob? job, QueuedMessage message)
    {
        if (job is not IUniqueJob unique) return;
        try
        {
            var gate = _rootProvider.GetService<IUniqueJobGate>();
            if (gate is not null) await gate.ReleaseAsync(unique.UniqueKey, message.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Naravel.Queue: could not release the unique key of job {JobId}; it stays locked until its TTL expires.", message.Id);
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
            var links = System.Text.Json.JsonSerializer.Deserialize(message.ChainedJobPayload, Naravel.Queue.Serialization.QueueJsonContext.Default.ChainLinks)!;
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
        Reservation reservation,
        CancellationToken stoppingToken,
        bool attemptAlreadyCounted = false)
    {
        reservation.FailureHandlingStarted = true;
        if (!attemptAlreadyCounted) message.Attempts++;
        message.Error = ex.ToString();

        if (message.Attempts >= message.MaxAttempts)
        {
            message.Connection ??= _options.Connection;
            await _failedJobStore.RecordAsync(message, ex, stoppingToken);
            var failedByUs = await driver.TryFailAsync(message, stoppingToken);
            reservation.Settle(); // failed, or fenced out: either way nothing is left for this worker to hand back
            if (!failedByUs)
            {
                // Fenced out: this worker outlived the visibility timeout and another worker now owns the job, so the job
                // is not ours to fail. Drop the record written just above and skip every failure side effect.
                await _failedJobStore.ForgetAsync(message.Id, stoppingToken);
                _logger.LogWarning(
                    "Naravel.Queue: job {JobId} on queue '{Queue}' exhausted its attempts, but this worker no longer owns the reservation; the failure was not recorded.",
                    message.Id, message.Queue);
                return;
            }

            await ReleaseUniqueAsync(job, message);
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
            if (delay > TimeSpan.Zero && !driver.SupportsDelayedDelivery)
            {
                // PDR-012a: the driver cannot hold one message back without blocking the others (Kafka), so the retry is
                // immediate. Say so once per job type instead of silently ignoring the configured backoff.
                if (_visibilityWarnings.TryAdd($"backoff:{message.JobType}", 0))
                {
                    _logger.LogWarning(
                        "Naravel.Queue: the driver of connection '{Connection}' does not support delayed delivery, so the backoff {Delay} of job type '{JobType}' is not applied and retries run immediately. Use a store whose driver supports delay (Redis, Database, RabbitMQ) if this job needs spaced retries.",
                        message.Connection ?? _options.Connection ?? _manager.DefaultConnectionName, delay, message.JobType);
                }
                delay = TimeSpan.Zero;
            }
            await driver.ReleaseAsync(message, delay, stoppingToken);
            reservation.Settle();
            QueueTelemetry.Retried.Add(1);
            _logger.LogWarning(ex, "Naravel.Queue: job {JobId} on queue '{Queue}' failed (attempt {Attempt}/{Max}), retrying in {Delay}.",
                message.Id, message.Queue, message.Attempts, message.MaxAttempts, delay);
            await NotifyAsync(listeners, l => l.OnRetryingAsync(message, ex, delay, stoppingToken));
        }
    }

    // Startup rules (PDR-012, D-13):
    //  - visibility timeout < worker default JobTimeout  -> ERROR (the job is reclaimed while it may still be running);
    //  - MaxJobTimeout set and visibility < MaxJobTimeout + VisibilityMargin -> WARNING only;
    //  - drivers without a visibility timeout (Memory, RabbitMQ, Kafka) and unset options are never validated.
    private void ValidateVisibilityTimeoutAtStartup()
    {
        var driver = _manager.Connection(_options.Connection);
        var visibilityTimeout = driver.VisibilityTimeout;
        if (!visibilityTimeout.HasValue) return;

        var connection = _options.Connection ?? _manager.DefaultConnectionName;

        if (_options.JobTimeout is { } jobTimeout && visibilityTimeout.Value < jobTimeout)
        {
            var message =
                $"Naravel.Queue: visibility timeout {visibilityTimeout.Value} on connection '{connection}' is shorter than the worker JobTimeout {jobTimeout}; " +
                "a running job would be reclaimed and executed twice. Raise the driver's visibility timeout or lower JobTimeout.";
            _logger.LogError("{Message}", message);
            throw new InvalidOperationException(message);
        }

        if (_options.MaxJobTimeout is { } maxJobTimeout &&
            visibilityTimeout.Value < maxJobTimeout + _options.VisibilityMargin)
        {
            _logger.LogWarning(
                "Naravel.Queue: visibility timeout {VisibilityTimeout} on connection '{Connection}' is shorter than MaxJobTimeout {MaxJobTimeout} plus VisibilityMargin {Margin}; a job that uses its full timeout may be reclaimed while running.",
                visibilityTimeout.Value, connection, maxJobTimeout, _options.VisibilityMargin);
        }
    }

    private void WarnIfVisibilityTimeoutIsTooShort(IQueueDriver driver, TimeSpan? jobTimeout, string scope)
    {
        var visibilityTimeout = driver.VisibilityTimeout;
        if (!visibilityTimeout.HasValue || !jobTimeout.HasValue ||
            visibilityTimeout.Value >= jobTimeout.Value + _options.VisibilityMargin)
            return;

        var connection = _options.Connection ?? _manager.DefaultConnectionName;
        if (!_visibilityWarnings.TryAdd($"{connection}:{scope}", 0)) return;

        _logger.LogWarning(
            "Naravel.Queue: visibility timeout {VisibilityTimeout} on connection '{Connection}' is shorter than job timeout {JobTimeout} plus margin {Margin}; the job may be reclaimed while running.",
            visibilityTimeout.Value, connection, jobTimeout.Value, _options.VisibilityMargin);
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
