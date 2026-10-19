using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Naravel.Database;

/// <summary>Wraps one command with an activity, metrics and the registered <see cref="IQueryListener"/>s.</summary>
internal sealed class QueryObserver(IEnumerable<IQueryListener> listeners)
{
    private readonly IQueryListener[] _listeners = listeners.ToArray();

    public static QueryObserver None { get; } = new([]);

    public async Task<T> ObserveAsync<T>(string connection, QueryOperation operation, string sql, IReadOnlyList<object?> bindings, Func<Task<T>> action)
    {
        var operationName = operation.ToString().ToLowerInvariant();
        var started = Stopwatch.GetTimestamp();
        using var activity = DatabaseTelemetry.ActivitySource.StartActivity("naravel.database.query");
        activity?.SetTag("naravel.database.connection", connection);
        activity?.SetTag("naravel.database.operation", operationName);

        Exception? failure = null;
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var tags = new TagList { { "connection", connection }, { "operation", operationName } };
            DatabaseTelemetry.Queries.Add(1, tags);
            DatabaseTelemetry.Duration.Record(elapsed.TotalMilliseconds, tags);
            if (failure is not null) DatabaseTelemetry.Failures.Add(1, tags);
            Notify(new QueryExecuted(connection, operation, sql, bindings, elapsed, failure));
        }
    }

    /// <summary>
    /// Observes a streamed command: one activity and one <see cref="QueryExecuted"/> for the whole enumeration, reported when it ends, fails or is disposed early.
    /// The duration therefore includes the time the consumer spends between rows.
    /// </summary>
    public async IAsyncEnumerable<T> ObserveStreamAsync<T>(string connection, QueryOperation operation, string sql, IReadOnlyList<object?> bindings, Func<IAsyncEnumerable<T>> source, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var operationName = operation.ToString().ToLowerInvariant();
        var started = Stopwatch.GetTimestamp();
        using var activity = DatabaseTelemetry.ActivitySource.StartActivity("naravel.database.query");
        activity?.SetTag("naravel.database.connection", connection);
        activity?.SetTag("naravel.database.operation", operationName);

        Exception? failure = null;
        try
        {
            await using var enumerator = source().GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool more;
                try
                {
                    more = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failure = exception;
                    activity?.SetStatus(ActivityStatusCode.Error);
                    throw;
                }

                if (!more) break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            var tags = new TagList { { "connection", connection }, { "operation", operationName } };
            DatabaseTelemetry.Queries.Add(1, tags);
            DatabaseTelemetry.Duration.Record(elapsed.TotalMilliseconds, tags);
            if (failure is not null) DatabaseTelemetry.Failures.Add(1, tags);
            Notify(new QueryExecuted(connection, operation, sql, bindings, elapsed, failure));
        }
    }

    private void Notify(QueryExecuted query)
    {
        foreach (var listener in _listeners)
        {
            try
            {
                listener.OnQueryExecuted(query);
            }
#pragma warning disable CA1031 // A broken listener must never fail a query.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }
    }
}
