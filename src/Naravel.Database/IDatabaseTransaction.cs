namespace Naravel.Database;

/// <summary>
/// A transaction started by <see cref="IDatabase.BeginTransactionAsync"/>. <see cref="Database"/> runs every query on the transaction's
/// connection; disposing the transaction without committing rolls it back.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>DB::beginTransaction()</c> with <c>commit()</c> / <c>rollBack()</c>.</para>
/// <para><b>Why it exists:</b> Laravel keeps the transaction on the connection (global state). Naravel hands you the transaction instead, so there is no
/// hidden ambient state to lose across <c>await</c>. Use <see cref="IDatabase.TransactionAsync{T}"/> when a callback is enough; use this when the
/// begin and the commit are in different places.</para>
/// <para><b>Not ported on purpose:</b> <c>afterCommit</c> callbacks and the transaction level counter; nesting is done with savepoints through
/// <c>TransactionAsync</c> on <see cref="Database"/>.</para>
/// </remarks>
public interface IDatabaseTransaction : IAsyncDisposable
{
    /// <summary>An <see cref="IDatabase"/> whose every query (builder and raw SQL) joins this transaction on one connection. Do not use it from several threads at once.</summary>
    IDatabase Database { get; }

    /// <summary>Commits the transaction. Laravel: <c>DB::commit</c>.</summary>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <exception cref="InvalidOperationException">The transaction was already committed or rolled back.</exception>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the transaction back. Laravel: <c>DB::rollBack</c>.</summary>
    /// <param name="cancellationToken">Cancels the rollback.</param>
    /// <exception cref="InvalidOperationException">The transaction was already committed or rolled back.</exception>
    Task RollBackAsync(CancellationToken cancellationToken = default);
}
