#pragma warning disable RS0026, RS0027 // The single-row and batch overloads mirror each other, as in Laravel.
namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>Inserts one row; every value is a bound parameter. Returns the rows inserted. Laravel: <c>insert</c> (which returns a boolean).</summary>
    /// <param name="row">Column names and values. An empty row inserts the column defaults.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="ArgumentException">A column name is not a plain identifier.</exception>
    /// <exception cref="InvalidOperationException">The query carries anything besides a table, or it is not connected.</exception>
    /// <remarks>An insert query may only have a table: a <c>where</c>, join, ordering or limit on it would be silently ignored in Laravel, so here it is an error.</remarks>
    public async Task<int> InsertAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        return await InsertCoreAsync([row], ignore: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts several rows, all with the same columns, in as few statements as the dialect allows: the batch is split to stay under the parameter limit (and 1000 rows per statement on SQL Server)
    /// and the statements run in one transaction, the ambient one or a new one, so the batch is all or nothing. Returns the rows inserted. Laravel: <c>insert</c> with an array of arrays.
    /// </summary>
    /// <param name="rows">The rows; each must have exactly the columns of the first. No rows inserts nothing and runs no command.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    public async Task<int> InsertAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return await InsertCoreAsync(rows.ToArray(), ignore: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts one row and skips it silently when it violates a unique key. Returns the rows inserted (0 when skipped). Laravel: <c>insertOrIgnore</c>.</summary>
    /// <param name="row">Column names and values.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="NotSupportedException">SQL Server has no single-statement way to do this; use <c>UpsertAsync</c> or catch the duplicate-key error.</exception>
    /// <remarks>SQLite writes <c>insert or ignore</c>, MySQL <c>insert ignore</c> (which also skips some other errors, such as a value that is too long, with a warning), PostgreSQL <c>on conflict do nothing</c>.</remarks>
    public async Task<int> InsertOrIgnoreAsync(IReadOnlyDictionary<string, object?> row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        return await InsertCoreAsync([row], ignore: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts several rows, skipping the ones that violate a unique key, under the same batching rules as <c>InsertAsync</c>. Returns the rows inserted. Laravel: <c>insertOrIgnore</c>.</summary>
    /// <param name="rows">The rows; each must have exactly the columns of the first.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="NotSupportedException">SQL Server.</exception>
    public async Task<int> InsertOrIgnoreAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return await InsertCoreAsync(rows.ToArray(), ignore: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts one row and returns the key the database generated for it. Laravel: <c>insertGetId</c>.</summary>
    /// <typeparam name="T">The key type, for example <see cref="long"/> or <see cref="Guid"/>.</typeparam>
    /// <param name="row">Column names and values.</param>
    /// <param name="idColumn">The generated column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <remarks>
    /// SQLite and PostgreSQL use <c>returning</c>, SQL Server <c>output inserted</c>; MySQL has neither, so a second command, <c>select last_insert_id()</c>, runs on the same connection (it is <c>0</c> when the table has no
    /// auto-increment column). The key is whatever the column holds, so this also works for a key you supply or a database-generated <see cref="Guid"/> on PostgreSQL and SQL Server.
    /// </remarks>
    public async Task<T> InsertGetIdAsync<T>(IReadOnlyDictionary<string, object?> row, string idColumn = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var id = SingleSegment(idColumn, nameof(idColumn));
        var runner = Connected();
        var (columns, values) = Normalize([row]);
        CheckInsertTarget();
        var compiled = Grammar.CompileInsertGetId(this, columns, values[0], id);

        return Grammar.LastInsertIdSql is { } followUp
            ? await runner.NonQueryThenScalarAsync<T>(compiled, followUp, cancellationToken).ConfigureAwait(false)
            : await runner.FirstValueAsync<T>(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Inserts the rows of a select: <c>insert into t (columns) select ...</c>. Returns the rows inserted. Laravel: <c>insertUsing</c>.</summary>
    /// <param name="columns">The columns to fill, in the order of the select's columns.</param>
    /// <param name="source">The select; it must use this query's grammar and its bindings are bound as parameters.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <remarks>MySQL rejects selecting from the table being inserted into only in some forms; the others are engine rules, not the builder's.</remarks>
    public async Task<int> InsertUsingAsync(string[] columns, Query source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(source);
        var runner = Connected();
        CheckInsertTarget();
        var compiled = CompileInsertUsing(columns, source);
        return await runner.NonQueryAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    internal IReadOnlyList<CompiledQuery> CompileInsert(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool ignore)
    {
        CheckInsertTarget();
        var (columns, values) = Normalize(rows);
        return values.Count == 0 ? [] : Grammar.CompileInsert(this, columns, values, ignore);
    }

    internal CompiledQuery CompileInsertGetId(IReadOnlyDictionary<string, object?> row, string idColumn)
    {
        CheckInsertTarget();
        var (columns, values) = Normalize([row]);
        return Grammar.CompileInsertGetId(this, columns, values[0], SingleSegment(idColumn, nameof(idColumn)));
    }

    internal CompiledQuery CompileInsertUsing(string[] columns, Query source)
    {
        CheckInsertTarget();
        if (source.Grammar.Dialect != Grammar.Dialect) throw new ArgumentException("The source query must use the same dialect as the target.", nameof(source));
        if (columns.Length == 0) throw new ArgumentException("At least one column is required.", nameof(columns));
        var names = columns.Select(column => SingleSegment(column, nameof(columns))).ToArray();
        return Grammar.CompileInsertUsing(this, names, source);
    }

    private async Task<int> InsertCoreAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool ignore, CancellationToken cancellationToken)
    {
        var runner = Connected();
        var statements = CompileInsert(rows, ignore);
        return statements.Count == 0 ? 0 : await runner.NonQueryManyAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    private static (string[] Columns, List<object?[]> Rows) Normalize(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return ([], []);

        var first = rows[0] ?? throw new ArgumentException("A row is null.", "rows");
        var columns = first.Keys.Select(column => SingleSegment(column, "rows")).ToArray();
        var values = new List<object?[]>(rows.Count);
        foreach (var row in rows)
        {
            if (row is null) throw new ArgumentException("A row is null.", "rows");
            if (row.Count != columns.Length || columns.Any(column => !row.ContainsKey(column)))
            {
                throw new ArgumentException($"Every row must have exactly the columns of the first row ({string.Join(", ", columns)}).", "rows");
            }

            values.Add(columns.Select(column => row[column]).ToArray());
        }

        return (columns, values);
    }

    private void CheckInsertTarget()
    {
        if (_table?.Alias is not null) throw new InvalidOperationException("An insert or upsert query cannot have a table alias: use the plain table name.");
        if (_unions.Count > 0 || _wheres.Count > 0 || _joins.Count > 0 || _groups.Count > 0 || _havings.Count > 0 || _orders.Count > 0 || _columns.Count > 0 || _distinct || _limit.HasValue || _offset.HasValue)
        {
            throw new InvalidOperationException("An insert query may only have a table: where, join, group, having, ordering, select, distinct, limit and offset do not apply to an insert and would be ignored. Start a new query with Table(...).");
        }
    }

    private static string SingleSegment(string column, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(column, parameterName);
        var identifier = Plain(IdentifierGrammar.Parse(column, parameterName), column, parameterName, allowAlias: false);
        return identifier.Segments.Count == 1 ? identifier.Segments[0] : throw IdentifierGrammar.Invalid(column, parameterName);
    }
}
