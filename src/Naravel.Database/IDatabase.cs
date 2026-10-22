namespace Naravel.Database;

#pragma warning disable RS0026, RS0027 // The generic overloads intentionally mirror the non-generic ones, as in Laravel.

/// <summary>
/// The entry point for running queries: <see cref="Table"/> starts a fluent <see cref="Query"/> that is already connected, and the
/// <c>*Async</c> methods run raw SQL with <c>?</c> placeholders.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the <c>DB</c> facade / <c>Connection</c> (<c>table</c>, <c>select</c>, <c>statement</c>, <c>affectingStatement</c>,
/// <c>unprepared</c>, <c>scalar</c>).</para>
/// <para><b>Why it exists:</b> the quickstart is <c>await db.Table("users").Where("active", true).GetAsync()</c>. Every method takes a
/// <see cref="CancellationToken"/>; values are always bound as parameters (a <c>?</c> in the SQL becomes <c>@p0</c>, <c>@p1</c>... and a literal question mark
/// is written <c>??</c>). Each call gets its connection and transaction from the <see cref="IConnectionScope"/> and never opens or closes one itself.</para>
/// <para><b>Not ported on purpose:</b> <c>DB::select</c> returning <c>stdClass</c> objects (rows are dictionaries or Dapper-mapped types) and <c>pretend()</c>.</para>
/// </remarks>
public interface IDatabase
{
    /// <summary>The connection name this instance is bound to, or <see langword="null"/> for the default connection.</summary>
    string? ConnectionName { get; }

    /// <summary>Gets an instance bound to a named connection (<see langword="null"/> for the default). Laravel: <c>DB::connection</c>.</summary>
    /// <param name="name">The connection name.</param>
    IDatabase Connection(string? name);

    /// <summary>Starts a query on a table, bound to this connection. Laravel: <c>DB::table</c>.</summary>
    /// <param name="table">The table name (<c>users</c>, <c>app.users</c> or <c>users as u</c>).</param>
    /// <exception cref="InvalidOperationException">No connection is configured, or the driver is created asynchronously and not yet resolved.</exception>
    Query Table(string table);

    /// <summary>Runs a raw select and returns each row as a case-insensitive dictionary. Laravel: <c>DB::select</c>.</summary>
    /// <param name="sql">SQL with <c>?</c> placeholders. Never build it from user input.</param>
    /// <param name="bindings">One value per <c>?</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> SelectAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a raw select and maps each row to <typeparamref name="T"/> with Dapper (not trim or AOT safe).</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="sql">SQL with <c>?</c> placeholders.</param>
    /// <param name="bindings">One value per <c>?</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<IReadOnlyList<T>> SelectAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a raw select and returns the first column of the first row, or the default of <typeparamref name="T"/> when there is no row. Laravel: <c>DB::scalar</c>.</summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="sql">SQL with <c>?</c> placeholders.</param>
    /// <param name="bindings">One value per <c>?</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<T> ScalarAsync<T>(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a statement (insert, update, DDL) and ignores the affected-row count. Failure throws. Laravel: <c>DB::statement</c>.</summary>
    /// <param name="sql">SQL with <c>?</c> placeholders.</param>
    /// <param name="bindings">One value per <c>?</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task StatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a statement and returns the number of affected rows. Laravel: <c>DB::affectingStatement</c>.</summary>
    /// <param name="sql">SQL with <c>?</c> placeholders.</param>
    /// <param name="bindings">One value per <c>?</c>.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<int> AffectingStatementAsync(string sql, IEnumerable<object?>? bindings = null, CancellationToken cancellationToken = default);

    /// <summary>Runs SQL exactly as written with no placeholder handling, for example a script of DDL. Laravel: <c>DB::unprepared</c>.</summary>
    /// <param name="sql">The SQL. Never build it from user input.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task UnpreparedAsync(string sql, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="callback"/> in a transaction: commit when it completes, roll back and rethrow when it throws. The <see cref="IDatabase"/>
    /// passed to the callback runs every query (builder or raw) on the transaction's connection. Laravel: <c>DB::transaction</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Nesting:</b> calling <c>TransactionAsync</c> on the callback's <see cref="IDatabase"/> opens a savepoint; a failure rolls back only to it.
    /// Savepoints are never retried.</para>
    /// <para><b>Deadlocks:</b> when <paramref name="attempts"/> is greater than one, an outermost transaction that fails with a deadlock or lock conflict
    /// (PostgreSQL 40P01 / 40001, MySQL 1213, SQL Server 1205, SQLite busy/locked) is rolled back and the whole callback runs again, so the callback must be repeatable.</para>
    /// <para><b>Why a parameter, not ambient state:</b> an <c>AsyncLocal</c> transaction does not flow back out of <c>await</c> and is invisible in a stack
    /// trace; passing the database makes "this runs in the transaction" explicit. A transaction cannot be started on a database borrowed from EF Core
    /// (neither side commits a transaction it did not start).</para>
    /// </remarks>
    /// <typeparam name="T">The callback's result type.</typeparam>
    /// <param name="callback">The work to run. Do not use the passed database from several threads at once.</param>
    /// <param name="attempts">How many times to try on a deadlock, 1 (no retry) by default.</param>
    /// <param name="cancellationToken">Cancels beginning and committing; a cancelled callback rolls back.</param>
    Task<T> TransactionAsync<T>(Func<IDatabase, Task<T>> callback, int attempts = 1, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="TransactionAsync{T}"/> for a callback without a result.</summary>
    /// <param name="callback">The work to run.</param>
    /// <param name="attempts">How many times to try on a deadlock, 1 (no retry) by default.</param>
    /// <param name="cancellationToken">Cancels beginning and committing; a cancelled callback rolls back.</param>
    Task TransactionAsync(Func<IDatabase, Task> callback, int attempts = 1, CancellationToken cancellationToken = default);

    /// <summary>Starts a transaction you commit yourself; <see cref="IDatabaseTransaction.Database"/> joins it. Laravel: <c>DB::beginTransaction</c>.</summary>
    /// <param name="cancellationToken">Cancels beginning the transaction.</param>
    /// <exception cref="InvalidOperationException">This instance is already inside a transaction (use <c>TransactionAsync</c> for a savepoint), or its connection is borrowed from EF Core.</exception>
    Task<IDatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
#pragma warning restore RS0026, RS0027
