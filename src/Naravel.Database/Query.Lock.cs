namespace Naravel.Database;

/// <summary>The row lock a select asks for.</summary>
internal enum LockMode
{
    None,
    Shared,
    Update,
}

public sealed partial class Query
{
    private LockMode _lock;

    internal LockMode Lock => _lock;

    /// <summary>
    /// Reads the selected rows with a <b>shared</b> lock: other transactions may read them but not change them until this transaction ends. Laravel: <c>sharedLock</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">Raised when the query is compiled (<see cref="Compile"/>, <see cref="ToSql"/> or running it) on SQLite, which has no row locks.</exception>
    /// <remarks>
    /// MySQL (8.0 or later) and PostgreSQL write <c>for share</c> at the end of the select; SQL Server writes the table hint <c>with (rowlock, holdlock)</c> after the table. A lock lives as long as the
    /// transaction, so call this inside one (outside a transaction every database frees it when the statement ends, so it protects nothing; the transactions arrive with R25). The last of
    /// <see cref="SharedLock"/> and <see cref="LockForUpdate"/> wins, and <see cref="Clone"/> copies the lock.
    /// <para>Where the lock lands differs, and these are the database's rules, not the builder's: MySQL and PostgreSQL lock the rows of every table the select reads, SQL Server locks only the table named by
    /// <c>Table(...)</c> (never a join, a <c>FromSub</c> or a <c>FromRaw</c>, for which it throws <see cref="NotSupportedException"/>), and PostgreSQL refuses a lock together with an aggregate, <c>Distinct</c>, <c>GroupBy</c>
    /// or the nullable side of an outer join. A lock on a query with <c>Union</c>, and a lock on a write (insert, update, delete, truncate), throw <see cref="InvalidOperationException"/> instead of being dropped silently.</para>
    /// </remarks>
    public Query SharedLock() => SetLock(LockMode.Shared);

    /// <summary>
    /// Reads the selected rows and <b>locks them for update</b>: other transactions can neither change them nor take a lock on them until this transaction ends. Laravel: <c>lockForUpdate</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">Raised when the query is compiled on SQLite, which has no row locks.</exception>
    /// <remarks>
    /// MySQL and PostgreSQL write <c>for update</c> at the end of the select; SQL Server writes <c>with (rowlock, updlock, holdlock)</c> after the table. See <see cref="SharedLock"/> for the transaction requirement and the
    /// differences between the databases.
    /// </remarks>
    public Query LockForUpdate() => SetLock(LockMode.Update);

    private Query SetLock(LockMode mode)
    {
        _lock = mode;
        return this;
    }
}
