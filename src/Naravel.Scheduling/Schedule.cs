namespace Naravel.Scheduling;

/// <summary>The set of named tasks a scheduler runs. Built once at startup in <c>AddNaravelScheduling</c>.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>Illuminate\Console\Scheduling\Schedule</c>. Not ported in this version: <c>command</c>, <c>exec</c> (process tasks), ping URLs, <c>runInBackground</c> and maintenance-mode awareness.</remarks>
public sealed class Schedule
{
    private readonly List<ScheduledTask> _tasks = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Creates an empty schedule.</summary>
    public Schedule()
    {
    }

    /// <summary>The tasks in the order they were added.</summary>
    public IReadOnlyList<ScheduledTask> Tasks => _tasks;

    /// <summary>
    /// Adds a task. The handler receives a scoped <see cref="IServiceProvider"/> (a new scope for every run) and the host's cancellation token.
    /// </summary>
    /// <param name="name">A unique name of letters, digits and <c>. _ : -</c>, at most 100 characters. It is the lock key and the metric tag, so it must stay stable across deploys.</param>
    /// <param name="handler">The work to run.</param>
    /// <exception cref="ArgumentException">The name is empty, too long, has other characters, or is already used.</exception>
    public ScheduledTask Call(string name, Func<IServiceProvider, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        CheckName(name);
        if (!_names.Add(name)) throw new ArgumentException($"A task named '{name}' is already scheduled.", nameof(name));
        var task = new ScheduledTask(name, handler);
        _tasks.Add(task);
        return task;
    }

    private static void CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 100) throw new ArgumentException("A task name is at most 100 characters.", nameof(name));
        foreach (var c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '-'))
            {
                throw new ArgumentException("A task name may contain only ASCII letters, digits and . _ : - (it is used in lock keys and metric tags).", nameof(name));
            }
        }
    }
}
