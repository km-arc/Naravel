#pragma warning disable RS0026, RS0027 // The returning methods mirror the plain write methods.
namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Inserts one row and returns the listed columns of the row as the database stored it (generated keys, defaults, triggers' changes). Laravel has no equivalent.
    /// </summary>
    /// <param name="row">The row to insert.</param>
    /// <param name="returning">The columns to read back, at least one, each a plain name.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="NotSupportedException">MySQL has neither <c>returning</c> nor <c>output</c>; use <c>InsertGetIdAsync</c> or a following select there.</exception>
    /// <remarks>SQLite and PostgreSQL write <c>returning</c>, SQL Server <c>output inserted</c>. The statement is a single insert.</remarks>
    public async Task<IReadOnlyDictionary<string, object?>> InsertReturningAsync(IReadOnlyDictionary<string, object?> row, string[] returning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rows = await InsertReturningAsync([row], returning, cancellationToken).ConfigureAwait(false);
        return rows[0];
    }

    /// <summary>Inserts the rows and returns the listed columns of every inserted row, in the order the database reports them. See the one-row form.</summary>
    /// <param name="rows">The rows, all with the same columns.</param>
    /// <param name="returning">The columns to read back, at least one, each a plain name.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="NotSupportedException">MySQL, which has neither <c>returning</c> nor <c>output</c>.</exception>
    /// <exception cref="InvalidOperationException">The rows need more than one statement (a dialect parameter or row limit): rows cannot be returned across statements, so split the batch yourself.</exception>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> InsertReturningAsync(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string[] returning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var runner = Connected();
        var statements = CompileInsertReturning(rows, returning);
        if (statements.Count == 0) return [];
        return await runner.RowsAsync(statements[0].Sql, statements[0].Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the matching rows and returns the listed columns of each updated row as they are <b>after</b> the update. Same rules as <see cref="UpdateAsync"/> (a <c>where</c> or <see cref="AllRows"/> is required).
    /// </summary>
    /// <param name="values">The columns to set.</param>
    /// <param name="returning">The columns to read back, at least one, each a plain name.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="NotSupportedException">MySQL, which has neither <c>returning</c> nor <c>output</c>.</exception>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> UpdateReturningAsync(IReadOnlyDictionary<string, object?> values, string[] returning, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var runner = Connected();
        var compiled = CompileUpdateReturning(values, returning);
        return await runner.RowsAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the matching rows and returns the listed columns of each deleted row as they were. Same rules as <see cref="DeleteAsync"/> (a <c>where</c> or <see cref="AllRows"/> is required).
    /// </summary>
    /// <param name="returning">The columns to read back, at least one, each a plain name.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="NotSupportedException">MySQL, which has neither <c>returning</c> nor <c>output</c>.</exception>
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> DeleteReturningAsync(string[] returning, CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        var compiled = CompileDeleteReturning(returning);
        return await runner.RowsAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    internal IReadOnlyList<CompiledQuery> CompileInsertReturning(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string[] returning)
    {
        var names = ReturningColumns(returning);
        CheckInsertTarget();
        var (columns, values) = Normalize(rows);
        if (values.Count == 0) return [];
        var statements = Grammar.CompileInsert(this, columns, values, false, null, names);
        if (statements.Count > 1)
        {
            throw new InvalidOperationException($"These {rows.Count} rows need {statements.Count} statements on {Grammar.Dialect} (parameter or row limit), and rows cannot be returned across statements. Insert them in smaller batches.");
        }

        return statements;
    }

    internal CompiledQuery CompileUpdateReturning(IReadOnlyDictionary<string, object?> values, string[] returning)
    {
        var names = ReturningColumns(returning);
        var assignments = values.Select(pair => Assignment(pair.Key, pair.Value, add: false)).ToList();
        if (assignments.Count == 0) throw new ArgumentException("At least one column to update is required.", nameof(values));
        CheckUpdateTarget();
        return Grammar.CompileUpdate(this, assignments, names);
    }

    internal CompiledQuery CompileDeleteReturning(string[] returning)
    {
        var names = ReturningColumns(returning);
        CheckDeleteTarget();
        return Grammar.CompileDelete(this, names);
    }

    private static string[] ReturningColumns(string[] returning)
    {
        ArgumentNullException.ThrowIfNull(returning);
        if (returning.Length == 0) throw new ArgumentException("At least one column to return is required.", nameof(returning));
        return returning.Select(column => SingleSegment(column, nameof(returning))).ToArray();
    }
}
