namespace Naravel.Database;

/// <summary>What an <see cref="IQueryListener"/> is told after a command ran (or failed).</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\Events\QueryExecuted</c> (<c>sql</c>, <c>bindings</c>, <c>time</c>, <c>connectionName</c>).</para>
/// <para><b>Why it exists:</b> in-process tooling such as a dev query log or a slow-query warning needs the SQL and the values.
/// Because the values can be sensitive, this object is never copied into telemetry: meter and activity tags carry the connection name and
/// <see cref="Operation"/> only.</para>
/// </remarks>
public sealed class QueryExecuted
{
    /// <summary>Creates the event.</summary>
    /// <param name="connectionName">The connection the command ran on.</param>
    /// <param name="operation">The kind of command.</param>
    /// <param name="sql">The SQL text with <c>@p0</c>, <c>@p1</c>... markers.</param>
    /// <param name="bindings">The values for the markers, in order.</param>
    /// <param name="duration">How long the command took.</param>
    /// <param name="exception">The failure, or <see langword="null"/> on success.</param>
    public QueryExecuted(string connectionName, QueryOperation operation, string sql, IReadOnlyList<object?> bindings, TimeSpan duration, Exception? exception = null)
    {
        ConnectionName = connectionName;
        Operation = operation;
        Sql = sql;
        Bindings = bindings;
        Duration = duration;
        Exception = exception;
    }

    /// <summary>The connection the command ran on.</summary>
    public string ConnectionName { get; }

    /// <summary>The kind of command.</summary>
    public QueryOperation Operation { get; }

    /// <summary>The SQL text. Values are never inlined; they are in <see cref="Bindings"/>.</summary>
    public string Sql { get; }

    /// <summary>The values bound to <c>@p0</c>, <c>@p1</c>... in order. May contain sensitive data: do not forward it to telemetry.</summary>
    public IReadOnlyList<object?> Bindings { get; }

    /// <summary>How long the command took, including getting the connection.</summary>
    public TimeSpan Duration { get; }

    /// <summary>The failure, or <see langword="null"/> when the command succeeded.</summary>
    public Exception? Exception { get; }
}
