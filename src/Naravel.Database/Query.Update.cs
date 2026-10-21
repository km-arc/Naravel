#pragma warning disable RS0026, RS0027 // The long and decimal overloads mirror each other, as in Laravel's increment.
namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Allows an update or delete without a <c>where</c> to touch every row. Without it those calls throw, so a forgotten <c>where</c> cannot rewrite a whole table.
    /// Naravel safety rule; Laravel has no equivalent and runs such a statement.
    /// </summary>
    public Query AllRows()
    {
        _allRows = true;
        return this;
    }

    /// <summary>
    /// Updates the matching rows with the given values, each bound as a parameter, and returns the rows changed. A key may be a JSON path (<c>options-&gt;theme</c>) to set one value inside a JSON column;
    /// several paths of one column are combined. Laravel: <c>update</c>.
    /// </summary>
    /// <param name="values">Column names and new values (<see langword="null"/> writes <c>NULL</c>); at least one.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">The query has no <c>where</c> (use <see cref="AllRows"/> or <c>UpdateAllAsync</c>), or it carries select, group, having, ordering, limit or offset.</exception>
    /// <exception cref="NotSupportedException">A join the dialect cannot express in an update (PostgreSQL and SQLite take inner joins only), or a JSON path set to null on SQL Server.</exception>
    /// <remarks>
    /// Joins in an update: MySQL <c>update t inner join o on ... set</c>, SQL Server <c>update t set ... from t inner join o on ...</c>, PostgreSQL and SQLite <c>update t set ... from o where ...</c>.
    /// Column names are plain (no table prefix), so a column that exists in both tables is ambiguous on MySQL and SQL Server: give the table an alias.
    /// JSON paths use <c>json_set</c> (SQLite, MySQL), <c>jsonb_set</c> (PostgreSQL) and <c>json_modify</c> (SQL Server); a <see langword="null"/> column stays <see langword="null"/> on SQLite and MySQL.
    /// </remarks>
    public async Task<int> UpdateAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        var compiled = CompileUpdate(values);
        return await runner.NonQueryAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Updates <b>every</b> row: the explicit form of <see cref="UpdateAsync"/> without a <c>where</c>. Returns the rows changed.</summary>
    /// <param name="values">Column names and new values; at least one.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> UpdateAllAsync(IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default) =>
        Clone().AllRows().UpdateAsync(values, cancellationToken);

    /// <summary>
    /// Inserts a row made of <paramref name="attributes"/> and <paramref name="values"/> when no row matches <paramref name="attributes"/> (each as <c>column = value</c>); otherwise updates the matching row with
    /// <paramref name="values"/>. Returns <see langword="true"/> when a row was inserted. Laravel: <c>updateOrInsert</c> (which always returns <c>true</c>).
    /// </summary>
    /// <param name="attributes">The columns that find the row; at least one. A <see langword="null"/> value matches <c>is null</c>.</param>
    /// <param name="values">The columns to write; may be empty, and wins over an attribute of the same name on insert.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <remarks>Two commands (an existence check, then an insert or update) that are <b>not atomic</b>: two callers can both see "missing" and both insert. Put a unique key on the attributes and use <c>UpsertAsync</c> when that matters. Any <c>where</c> already on the query narrows the match as well.</remarks>
    public async Task<bool> UpdateOrInsertAsync(IReadOnlyDictionary<string, object?> attributes, IReadOnlyDictionary<string, object?> values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(values);
        if (attributes.Count == 0) throw new ArgumentException("At least one attribute is required to find the row.", nameof(attributes));
        _ = Connected();

        var probe = Clone();
        foreach (var attribute in attributes) probe.Where(attribute.Key, attribute.Value);

        if (!await probe.ExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(attributes);
            foreach (var value in values) row[value.Key] = value.Value;
            await NewTableQuery().InsertAsync(row, cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (values.Count > 0) await probe.UpdateAsync(values, cancellationToken).ConfigureAwait(false);
        return false;
    }

    /// <summary>Adds <paramref name="amount"/> to a column in the matching rows (<c>set col = col + ?</c>) in one statement, so concurrent increments do not lose updates. Returns the rows changed. Laravel: <c>increment</c>.</summary>
    /// <param name="column">The numeric column.</param>
    /// <param name="amount">What to add, 1 by default; may be negative.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">No <c>where</c>, as for <see cref="UpdateAsync"/>.</exception>
    public Task<int> IncrementAsync(string column, long amount = 1, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default) =>
        AddAsync(new Dictionary<string, object?> { [Required(column)] = amount }, extra, cancellationToken);

    /// <summary>Like <see cref="IncrementAsync(string, long, IReadOnlyDictionary{string, object}, CancellationToken)"/> with a decimal amount, for decimal and floating-point columns.</summary>
    /// <param name="column">The numeric column.</param>
    /// <param name="amount">What to add; may be negative.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> IncrementAsync(string column, decimal amount, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default) =>
        AddAsync(new Dictionary<string, object?> { [Required(column)] = amount }, extra, cancellationToken);

    /// <summary>Subtracts <paramref name="amount"/> from a column in the matching rows. Returns the rows changed. Laravel: <c>decrement</c>.</summary>
    /// <param name="column">The numeric column.</param>
    /// <param name="amount">What to subtract, 1 by default.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> DecrementAsync(string column, long amount = 1, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default) =>
        AddAsync(new Dictionary<string, object?> { [Required(column)] = checked(-amount) }, extra, cancellationToken);

    /// <summary>Like <see cref="DecrementAsync(string, long, IReadOnlyDictionary{string, object}, CancellationToken)"/> with a decimal amount.</summary>
    /// <param name="column">The numeric column.</param>
    /// <param name="amount">What to subtract.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> DecrementAsync(string column, decimal amount, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default) =>
        AddAsync(new Dictionary<string, object?> { [Required(column)] = -amount }, extra, cancellationToken);

    /// <summary>Adds an amount to several columns in one statement. Returns the rows changed. Laravel: <c>incrementEach</c>.</summary>
    /// <param name="columns">Each column and what to add to it; at least one.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> IncrementEachAsync(IReadOnlyDictionary<string, long> columns, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return AddAsync(columns.ToDictionary(pair => pair.Key, pair => (object?)pair.Value), extra, cancellationToken);
    }

    /// <summary>Subtracts an amount from several columns in one statement. Returns the rows changed. Laravel: <c>decrementEach</c>.</summary>
    /// <param name="columns">Each column and what to subtract from it; at least one.</param>
    /// <param name="extra">Other columns to set in the same statement.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> DecrementEachAsync(IReadOnlyDictionary<string, long> columns, IReadOnlyDictionary<string, object?>? extra = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        return AddAsync(columns.ToDictionary(pair => pair.Key, pair => (object?)checked(-pair.Value)), extra, cancellationToken);
    }

    internal CompiledQuery CompileUpdate(IReadOnlyDictionary<string, object?> values) => CompileUpdate(values, null);

    internal CompiledQuery CompileUpdate(IReadOnlyDictionary<string, object?>? values, IReadOnlyDictionary<string, object?>? additions)
    {
        var assignments = new List<UpdateAssignment>();
        if (values is not null) assignments.AddRange(values.Select(pair => Assignment(pair.Key, pair.Value, add: false)));
        if (additions is not null) assignments.AddRange(additions.Select(pair => Assignment(pair.Key, pair.Value, add: true)));
        if (assignments.Count == 0) throw new ArgumentException("At least one column to update is required.", nameof(values));
        CheckUpdateTarget();
        return Grammar.CompileUpdate(this, assignments);
    }

    private async Task<int> AddAsync(IReadOnlyDictionary<string, object?> additions, IReadOnlyDictionary<string, object?>? extra, CancellationToken cancellationToken)
    {
        var runner = Connected();
        if (additions.Count == 0) throw new ArgumentException("At least one column is required.", "columns");
        var compiled = CompileUpdate(extra, additions);
        return await runner.NonQueryAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    private static UpdateAssignment Assignment(string key, object? value, bool add)
    {
        ArgumentNullException.ThrowIfNull(key);
        var identifier = Plain(IdentifierGrammar.Parse(key, "values"), key, "values", allowAlias: false, allowJson: !add);
        if (identifier.Segments.Count != 1) throw IdentifierGrammar.Invalid(key, "values");
        return new UpdateAssignment(identifier.Segments[0], identifier.JsonPath, value, add);
    }

    private void CheckUpdateTarget()
    {
        if (_table is null) throw new InvalidOperationException("The query has no table. Call Table(...) before updating.");
        if (_fromSubquery is not null || HasSubqueryJoin || _unions.Count > 0 || _columns.Count > 0 || _groups.Count > 0 || _havings.Count > 0 || _orders.Count > 0 || _distinct || _limit.HasValue || _offset.HasValue)
        {
            throw new InvalidOperationException("An update query may only have a table, joins and where: select, group, having, ordering, distinct, limit and offset do not apply to an update.");
        }

        if (_wheres.Count == 0 && !_allRows)
        {
            throw new InvalidOperationException("This update has no where, so it would change every row. Add a Where(...), or call AllRows() or UpdateAllAsync to confirm that you mean every row.");
        }
    }

    private Query NewTableQuery() => new(Grammar) { _table = _table, Runner = Runner };
}
