using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Locks;

namespace Naravel.Scheduling.Testing;

/// <summary>
/// Runs a <see cref="Schedule"/> at times you choose, with no host, no timer and no waiting, and records what happened, so a test can say "this task is due at 08:00 on weekdays".
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none (Laravel tests call <c>schedule:run</c> or travel in time). This is the Naravel fake required of every module.</para>
/// <para>Task handlers receive an empty service provider with no services, so a task under test should not need any, or use <c>Naravel.Scheduling</c> through the real host instead.</para>
/// </remarks>
public sealed class SchedulerFake
{
    private readonly Scheduler _scheduler;
    private readonly List<KeyValuePair<string, string>> _outcomes = [];

    /// <summary>Builds the fake. With no lock the fake uses a process-local one that reports itself as shared, so <c>OnOneServer</c> can be tested; pass your own lock to test two fakes over one lock.</summary>
    /// <param name="configure">Adds the tasks.</param>
    /// <param name="locks">The lock driver, or null for a private in-memory one.</param>
    public SchedulerFake(Action<Schedule> configure, ICacheLock? locks = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Schedule = new Schedule();
        configure(Schedule);
        _scheduler = new Scheduler(
            Schedule,
            locks ?? new SharedMemoryLock(),
            new EmptyServices(),
            new SchedulingOptions(),
            TimeProvider.System,
            NullLogger.Instance,
            (name, outcome) =>
            {
                lock (_outcomes) _outcomes.Add(new KeyValuePair<string, string>(name, outcome));
            });
    }

    /// <summary>The schedule that was built.</summary>
    public Schedule Schedule { get; }

    /// <summary>Every run so far as (task name, outcome) pairs, in the order they finished. Outcomes: <c>succeeded</c>, <c>failed</c>, <c>cancelled</c>, <c>skipped_overlap</c>, <c>skipped_other_server</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Outcomes
    {
        get
        {
            lock (_outcomes) return _outcomes.ToArray();
        }
    }

    /// <summary>Whether the named task is due in the minute that contains <paramref name="when"/>.</summary>
    public bool IsDue(string name, DateTimeOffset when) => _scheduler.IsDue(name, when);

    /// <summary>The task list as the scheduler reports it.</summary>
    public IReadOnlyList<ScheduledTaskInfo> List() => _scheduler.List();

    /// <summary>Checks the setup the way the host does at startup.</summary>
    /// <exception cref="InvalidOperationException">A task uses <c>OnOneServer</c> with a lock that is not shared.</exception>
    public void Validate() => _scheduler.Validate();

    /// <summary>Runs every task due in the minute that contains <paramref name="when"/> and waits for them.</summary>
    public Task RunAtAsync(DateTimeOffset when, CancellationToken cancellationToken = default) => _scheduler.RunDueAsync(when, cancellationToken);

    /// <summary>Throws when the task has not finished with the <c>succeeded</c> outcome at least once.</summary>
    /// <exception cref="InvalidOperationException">The task did not run.</exception>
    public void AssertRan(string name)
    {
        if (!Outcomes.Any(o => o.Key == name && o.Value == "succeeded"))
        {
            throw new InvalidOperationException($"Scheduled task '{name}' did not run successfully. Runs: {Describe()}.");
        }
    }

    /// <summary>Throws when the task finished with any outcome.</summary>
    /// <exception cref="InvalidOperationException">The task ran or was skipped by a guard.</exception>
    public void AssertNotRan(string name)
    {
        if (Outcomes.Any(o => o.Key == name))
        {
            throw new InvalidOperationException($"Scheduled task '{name}' was expected not to run. Runs: {Describe()}.");
        }
    }

    private string Describe()
    {
        var all = Outcomes;
        return all.Count == 0 ? "none" : string.Join(", ", all.Select(o => $"{o.Key}={o.Value}"));
    }

    /// <summary>A process-local lock that claims to be shared, so a fake can exercise <c>OnOneServer</c>. Each fake gets its own prefix unless a lock is passed in.</summary>
    private sealed class SharedMemoryLock : ICacheLock
    {
        private readonly MemoryLock _inner = new("fake-" + Guid.NewGuid().ToString("N"));

        public bool IsShared => true;

        public Task<string?> AcquireAsync(string name, TimeSpan ttl, CancellationToken cancellationToken = default) => _inner.AcquireAsync(name, ttl, cancellationToken);

        public Task<bool> ReleaseAsync(string name, string token, CancellationToken cancellationToken = default) => _inner.ReleaseAsync(name, token, cancellationToken);
    }

    private sealed class EmptyServices : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) => serviceType == typeof(IServiceScopeFactory) ? this : null;

        public IServiceScope CreateScope() => this;

        public void Dispose()
        {
        }
    }
}
