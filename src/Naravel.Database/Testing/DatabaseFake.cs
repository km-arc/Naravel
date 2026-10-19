using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Naravel.Database.Testing;

#pragma warning disable RS0026, RS0027 // The generic overloads intentionally mirror the non-generic ones, as in IDatabase.

/// <summary>
/// An in-memory <see cref="IDatabase"/> that records every command and returns canned results, for tests that do not need a database.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> there is no <c>DB::fake()</c>; the closest are <c>DB::pretend()</c> (record SQL without running it) and mocking the connection.</para>
/// <para><b>Why it exists:</b> inject it where <see cref="IDatabase"/> is expected. <c>Table(...)</c> compiles real SQL with the chosen dialect's grammar, so
/// you can assert on the SQL and the bindings; the results come from what you set up with <see cref="ReturnRows"/>, <see cref="ReturnObjects{T}"/>,
/// <see cref="ReturnScalar"/> and <see cref="ReturnAffected"/>. Without a set-up, selects return no rows, scalars the default value and statements 0 affected rows.</para>
/// <para><b>Not ported on purpose:</b> it does not parse or execute SQL. Behaviour that depends on a real engine (constraints, joins, collation) needs a SQLite or server test.</para>
/// </remarks>
public sealed class DatabaseFake : IDatabase, IQueryRunner
{
    private readonly DatabaseDialect _dialect;
    private readonly ConcurrentQueue<QueryExecuted> _executed;
    private readonly ConcurrentQueue<Setup> _setups;

    /// <summary>Creates a fake that compiles <c>Table(...)</c> queries for a dialect.</summary>
    /// <param name="dialect">The dialect whose grammar compiles the SQL, SQLite by default.</param>
    public DatabaseFake(DatabaseDialect dialect = DatabaseDialect.Sqlite)
        : this(dialect, null, new ConcurrentQueue<QueryExecuted>(), new ConcurrentQueue<Setup>())
    {
    }

    private DatabaseFake(DatabaseDialect dialect, string? connectionName, ConcurrentQueue<QueryExecuted> executed, ConcurrentQueue<Setup> setups)
    {
        _dialect = dialect;
        ConnectionName = connectionName;
        _executed = executed;
        _setups = setups;
    }

    /// <inheritdoc />
    public string? ConnectionName { get; }

    /// <summary>Every command run so far, oldest first, across all <see cref="Connection"/> instances of this fake.</summary>
    public IReadOnlyList<QueryExecuted> Executed => _executed.ToArray();

    /// <inheritdoc />
    public IDatabase Connection(string? name) => new DatabaseFake(_dialect, name, _executed, _setups);

    /// <inheritdoc />
    public Query Table(string table) => new Query(Grammar.For(_dialect)) { Runner = this }.Table(table);

    /// <summary>Makes selects whose SQL contains <paramref name="sqlContains"/> return these rows (the latest matching set-up wins).</summary>
    /// <param name="rows">The rows, each a column-name to value map.</param>
    /// <param name="sqlContains">A case-insensitive fragment of the SQL, or <see langword="null"/> to match every select.</param>
    public DatabaseFake ReturnRows(IEnumerable<IReadOnlyDictionary<string, object?>> rows, string? sqlContains = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _setups.Enqueue(new Setup(sqlContains, QueryOperation.Select, rows.Select(row => (object?)new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase)).ToArray()));
        return this;
    }

    /// <summary>Makes mapped selects (<c>GetAsync&lt;T&gt;</c>, <c>SelectAsync&lt;T&gt;</c>) return these objects as they are.</summary>
    /// <typeparam name="T">The row type the code under test asks for.</typeparam>
    /// <param name="rows">The objects to return.</param>
    /// <param name="sqlContains">A case-insensitive fragment of the SQL, or <see langword="null"/> to match every select of that type.</param>
    public DatabaseFake ReturnObjects<T>(IEnumerable<T> rows, string? sqlContains = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _setups.Enqueue(new Setup(sqlContains, QueryOperation.Select, rows.Cast<object?>().ToArray(), typeof(T)));
        return this;
    }

    /// <summary>Makes scalar reads (<c>ScalarAsync</c>, <c>ValueAsync</c>) return this value, converted to the requested type.</summary>
    /// <param name="value">The value.</param>
    /// <param name="sqlContains">A case-insensitive fragment of the SQL, or <see langword="null"/> to match every scalar read.</param>
    public DatabaseFake ReturnScalar(object? value, string? sqlContains = null)
    {
        _setups.Enqueue(new Setup(sqlContains, QueryOperation.Scalar, new[] { value }));
        return this;
    }

    /// <summary>Makes statements return this affected-row count.</summary>
    /// <param name="count">The count.</param>
    /// <param name="sqlContains">A case-insensitive fragment of the SQL, or <see langword="null"/> to match every statement.</param>
    public DatabaseFake ReturnAffected(int count, string? sqlContains = null)
    {
        _setups.Enqueue(new Setup(sqlContains, QueryOperation.Statement, new object?[] { count }));
        return this;
    }

    /// <summary>Asserts that a command (matching <paramref name="predicate"/> when given) ran, and returns the first match.</summary>
    /// <param name="predicate">Optional filter.</param>
    /// <exception cref="InvalidOperationException">No such command ran.</exception>
    public QueryExecuted AssertExecuted(Func<QueryExecuted, bool>? predicate = null) =>
        _executed.FirstOrDefault(q => predicate is null || predicate(q))
        ?? throw new InvalidOperationException(predicate is null
            ? "Expected a database command to run, but none did."
            : $"No database command matched the predicate; {_executed.Count} command(s) ran: {Describe()}");

    /// <summary>Asserts that exactly <paramref name="expectedCount"/> commands ran.</summary>
    /// <param name="expectedCount">The expected number of commands.</param>
    /// <exception cref="InvalidOperationException">The count differs.</exception>
    public IReadOnlyList<QueryExecuted> AssertExecutedCount(int expectedCount)
    {
        var executed = _executed.ToArray();
        return executed.Length == expectedCount
            ? executed
            : throw new InvalidOperationException($"Expected {expectedCount} database command(s), but found {executed.Length}: {Describe()}");
    }

    /// <summary>Asserts that no command ran.</summary>
    /// <exception cref="InvalidOperationException">At least one command ran.</exception>
    public void AssertNothingExecuted()
    {
        if (!_executed.IsEmpty)
        {
            throw new InvalidOperationException($"Expected no database command, but {_executed.Count} ran: {Describe()}");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SelectAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return ((IQueryRunner)this).RowsAsync(text, values, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<T>> SelectAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return ((IQueryRunner)this).RowsAsync<T>(text, values, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> ScalarAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return ((IQueryRunner)this).FirstValueAsync<T>(text, values, cancellationToken);
    }

    /// <inheritdoc />
    public async Task StatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default) =>
        await AffectingStatementAsync(sql, bindings, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<int> AffectingStatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default)
    {
        var (text, values) = DatabaseExecutor.Prepare(sql, bindings);
        return ((IQueryRunner)this).NonQueryAsync(text, values, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnpreparedAsync(string sql, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sql);
        await ((IQueryRunner)this).NonQueryAsync(sql, Array.Empty<object?>(), cancellationToken).ConfigureAwait(false);
    }

    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> IQueryRunner.RowsAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        Record(QueryOperation.Select, sql, bindings, cancellationToken);
        var rows = Find(QueryOperation.Select, sql, type: null)?.Items.OfType<IReadOnlyDictionary<string, object?>>().ToArray()
            ?? [];
        return Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, object?>>>(rows);
    }

    Task<IReadOnlyList<T>> IQueryRunner.RowsAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        Record(QueryOperation.Select, sql, bindings, cancellationToken);
        var setup = Find(QueryOperation.Select, sql, typeof(T));
        if (setup is null) return Task.FromResult<IReadOnlyList<T>>([]);
        return Task.FromResult<IReadOnlyList<T>>(setup.Items.Cast<T>().ToArray());
    }

    async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> IQueryRunner.StreamAsync(string sql, IReadOnlyList<object?> bindings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in await ((IQueryRunner)this).RowsAsync(sql, bindings, cancellationToken).ConfigureAwait(false)) yield return row;
    }

    async IAsyncEnumerable<T> IQueryRunner.StreamAsync<T>(string sql, IReadOnlyList<object?> bindings, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var row in await ((IQueryRunner)this).RowsAsync<T>(sql, bindings, cancellationToken).ConfigureAwait(false)) yield return row;
    }

    Task<T> IQueryRunner.FirstValueAsync<T>(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        Record(QueryOperation.Scalar, sql, bindings, cancellationToken);
        var value = Find(QueryOperation.Scalar, sql, type: null)?.Items[0];
        return Task.FromResult(Convert<T>(value));
    }

    Task<int> IQueryRunner.NonQueryAsync(string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        Record(QueryOperation.Statement, sql, bindings, cancellationToken);
        var affected = Find(QueryOperation.Statement, sql, type: null)?.Items[0];
        return Task.FromResult(affected is int count ? count : 0);
    }

    private static T Convert<T>(object? value)
    {
        if (value is null) return default!;
        if (value is T typed) return typed;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private void Record(QueryOperation operation, string sql, IReadOnlyList<object?> bindings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _executed.Enqueue(new QueryExecuted(ConnectionName ?? "default", operation, sql, bindings, TimeSpan.Zero));
    }

    private Setup? Find(QueryOperation operation, string sql, Type? type) =>
        _setups.Reverse().FirstOrDefault(s =>
            s.Operation == operation
            && s.ItemType == type
            && (s.SqlContains is null || sql.Contains(s.SqlContains, StringComparison.OrdinalIgnoreCase)));

    private string Describe() => string.Join("; ", _executed.Select(q => q.Sql));

    private sealed record Setup(string? SqlContains, QueryOperation Operation, object?[] Items, Type? ItemType = null);
}
#pragma warning restore RS0026, RS0027
