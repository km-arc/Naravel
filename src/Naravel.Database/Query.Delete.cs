#pragma warning disable RS0026, RS0027 // DeleteAsync mirrors Laravel's delete with and without an id.
namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Deletes the matching rows and returns how many were deleted. <b>It requires a <c>where</c></b>: a delete without one throws, so a forgotten <c>where</c> cannot empty a table
    /// (Laravel would run it). To delete every row on purpose call <c>DeleteAllAsync</c> or <see cref="AllRows"/>; to empty a table and reset its counter use <c>TruncateAsync</c>. Laravel: <c>delete</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">No <c>where</c>, or the query carries select, group, having, ordering, distinct, limit or offset.</exception>
    /// <exception cref="NotSupportedException">PostgreSQL does not take a left, right or cross join in a delete.</exception>
    /// <remarks>
    /// Joins in a delete: MySQL and SQL Server write <c>delete t from t inner join o on ... where ...</c>, PostgreSQL <c>delete from t using o where ...</c> (inner joins only), and SQLite, which has no joined delete,
    /// <c>delete from t where rowid in (select t.rowid from t inner join ...)</c> (so a table declared <c>WITHOUT ROWID</c> cannot be used).
    /// </remarks>
    public async Task<int> DeleteAsync(CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        var compiled = CompileDelete();
        return await runner.NonQueryAsync(compiled.Sql, compiled.Bindings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes the row whose key column equals <paramref name="id"/> and returns how many were deleted (0 or 1). Laravel: <c>delete($id)</c>.</summary>
    /// <param name="id">The key value, bound as a parameter.</param>
    /// <param name="keyColumn">The key column, <c>id</c> by default.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> DeleteByIdAsync(object id, string keyColumn = "id", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(id);
        return Clone().Where(keyColumn, id).DeleteAsync(cancellationToken);
    }

    /// <summary>Deletes <b>every</b> row: the explicit form of <see cref="DeleteAsync"/> without a <c>where</c>. Returns how many were deleted.</summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> DeleteAllAsync(CancellationToken cancellationToken = default) => Clone().AllRows().DeleteAsync(cancellationToken);

    /// <summary>
    /// Empties the table and resets its identity counter, so the next generated key starts again from the beginning. Laravel: <c>truncate</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">The query carries anything besides a table.</exception>
    /// <remarks>
    /// MySQL <c>truncate table</c>, SQL Server <c>truncate table</c>, PostgreSQL <c>truncate table ... restart identity</c> (<b>without</b> <c>cascade</c>, which Laravel adds: a table that foreign keys point at cannot be truncated here, so
    /// no referencing table is emptied by surprise), SQLite <c>delete from</c> plus a reset of its <c>sqlite_sequence</c> row in one transaction when the database has that table. Truncating is DDL on most engines: it
    /// cannot be rolled back on MySQL, and SQL Server and MySQL refuse it for a table referenced by a foreign key.
    /// </remarks>
    public async Task TruncateAsync(CancellationToken cancellationToken = default)
    {
        var runner = Connected();
        CheckTruncateTarget();

        var resetSequence = false;
        if (Grammar.Dialect == DatabaseDialect.Sqlite)
        {
            resetSequence = await runner.FirstValueAsync<long>("select count(*) from sqlite_master where type = 'table' and name = 'sqlite_sequence'", Array.Empty<object?>(), cancellationToken).ConfigureAwait(false) > 0;
        }

        await runner.NonQueryManyAsync(Grammar.CompileTruncate(this, resetSequence), cancellationToken).ConfigureAwait(false);
    }

    internal CompiledQuery CompileDelete()
    {
        CheckDeleteTarget();
        return Grammar.CompileDelete(this);
    }

    private void CheckDeleteTarget()
    {
        if (_table is null) throw new InvalidOperationException("The query has no table. Call Table(...) before deleting.");
        if (_columns.Count > 0 || _groups.Count > 0 || _havings.Count > 0 || _orders.Count > 0 || _distinct || _limit.HasValue || _offset.HasValue)
        {
            throw new InvalidOperationException("A delete query may only have a table, joins and where: select, group, having, ordering, distinct, limit and offset do not apply to a delete.");
        }

        if (_wheres.Count == 0 && !_allRows)
        {
            throw new InvalidOperationException("This delete has no where, so it would remove every row. Add a Where(...), or call AllRows() or DeleteAllAsync to confirm that you mean every row, or TruncateAsync to empty the table and reset its counter.");
        }
    }

    internal IReadOnlyList<CompiledQuery> CompileTruncate(bool resetSequence)
    {
        CheckTruncateTarget();
        return Grammar.CompileTruncate(this, resetSequence);
    }

    private void CheckTruncateTarget()
    {
        if (_table is null) throw new InvalidOperationException("The query has no table. Call Table(...) before truncating.");
        if (_table.Alias is not null || _wheres.Count > 0 || _joins.Count > 0 || _columns.Count > 0 || _groups.Count > 0 || _havings.Count > 0 || _orders.Count > 0 || _distinct || _limit.HasValue || _offset.HasValue)
        {
            throw new InvalidOperationException("A truncate query may only have a table: it empties the whole table, so a where, join, alias, select, ordering or limit would be misleading. Use DeleteAsync with a where to remove some rows.");
        }
    }
}
