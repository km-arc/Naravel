namespace Naravel.Sample.App.Listeners;

/// <summary>
/// A tiny in-memory log used by the sample listeners so the web page can show which listeners ran and in what order.
/// It is a singleton because the queued listener runs later, in a worker scope, in the same process.
/// </summary>
public sealed class EventTrace
{
    private readonly object _gate = new();
    private readonly List<string> _entries = new();

    public void Add(string entry)
    {
        lock (_gate)
        {
            _entries.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {entry}");
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
