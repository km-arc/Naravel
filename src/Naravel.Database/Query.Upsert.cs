#pragma warning disable RS0026, RS0027 // The single-row and batch overloads mirror each other, as in Laravel.
namespace Naravel.Database;

public sealed partial class Query
{
    /// <summary>
    /// Inserts the rows, and where a row collides with an existing one on <paramref name="uniqueBy"/> updates that row instead, in one statement per batch.
    /// Returns the rows affected (MySQL counts an updated row twice). Laravel: <c>upsert</c>.
    /// </summary>
    /// <param name="rows">The rows; each must have exactly the columns of the first. No rows runs no command.</param>
    /// <param name="uniqueBy">The columns that identify a row; at least one, all among the row columns. They must be covered by a unique index (PostgreSQL and SQLite <c>on conflict</c>, SQL Server <c>merge ... on</c>); MySQL ignores them and reacts to <b>any</b> unique key.</param>
    /// <param name="update">The columns to overwrite on a collision, all among the row columns; <see langword="null"/> overwrites every column. An empty list makes this a plain insert, as in Laravel.</param>
    /// <param name="cancellationToken">Cancels the commands.</param>
    /// <exception cref="ArgumentException">A column is not a plain name, a key or update column is not in the rows, or <paramref name="uniqueBy"/> is empty.</exception>
    /// <remarks>
    /// <para>SQLite and PostgreSQL write <c>on conflict (...) do update set c = excluded.c</c>; MySQL (8.0.19 or later) <c>as naravel_upsert on duplicate key update c = naravel_upsert.c</c>; SQL Server a
    /// <c>merge ... with (holdlock)</c>, which is atomic under concurrency. A batch is split at the parameter and row limits and runs in one transaction, like <c>InsertAsync</c>.</para>
    /// <para>Inside one statement the rows must have distinct keys: PostgreSQL and SQL Server refuse to change one row twice. Not ported: an <c>update</c> given as column expressions; use <c>UpdateAsync</c> for that.</para>
    /// </remarks>
    public async Task<int> UpsertAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows, IEnumerable<string> uniqueBy, IEnumerable<string>? update = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(uniqueBy);
        var runner = Connected();
        var statements = CompileUpsert(rows.ToArray(), uniqueBy.ToArray(), update?.ToArray());
        return statements.Count == 0 ? 0 : await runner.NonQueryManyAsync(statements, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Upserts one row. Returns the rows affected. Laravel: <c>upsert</c>.</summary>
    /// <param name="row">The row.</param>
    /// <param name="uniqueBy">The columns that identify the row; see <c>UpsertAsync</c> with several rows.</param>
    /// <param name="update">The columns to overwrite on a collision, or <see langword="null"/> for every column.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public Task<int> UpsertAsync(IReadOnlyDictionary<string, object?> row, IEnumerable<string> uniqueBy, IEnumerable<string>? update = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        return UpsertAsync([row], uniqueBy, update, cancellationToken);
    }

    internal IReadOnlyList<CompiledQuery> CompileUpsert(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<string> uniqueBy, IReadOnlyList<string>? update)
    {
        CheckInsertTarget();
        var (columns, values) = Normalize(rows);
        var keys = uniqueBy.Select(column => SingleSegment(column, nameof(uniqueBy))).ToArray();
        if (keys.Length == 0) throw new ArgumentException("At least one column is required to identify a row.", nameof(uniqueBy));
        var names = update?.Select(column => SingleSegment(column, nameof(update))).ToArray();
        foreach (var column in keys.Concat(names ?? []))
        {
            if (values.Count > 0 && !columns.Contains(column, StringComparer.Ordinal)) throw new ArgumentException($"The column '{column}' is not one of the row columns ({string.Join(", ", columns)}).", nameof(rows));
        }

        if (values.Count == 0) return [];
        if (names is { Length: 0 }) return Grammar.CompileInsert(this, columns, values, ignore: false);
        return Grammar.CompileUpsert(this, columns, values, keys, names ?? columns);
    }
}
