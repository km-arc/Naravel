using System.Data.Common;

namespace Naravel.Database;

/// <summary>
/// Hands out connection leases. Every query the builder runs gets its connection and transaction from a lease; the builder never
/// opens or closes a connection by itself.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> none directly; Laravel's <c>Connection</c> holds the PDO and the transaction level together.</para>
/// <para><b>Why it exists (PDR-022 A1):</b> transactions and EF Core interop arrive in R25. If the executor were written as
/// "open, run, close" they would need a breaking change. With a scope, R25 adds an ambient-transaction scope and an EF Core scope
/// without touching <c>Query</c> or any execution signature. This stage ships only the simple pooled scope.</para>
/// </remarks>
public interface IConnectionScope
{
    /// <summary>Gets a lease for the named connection (the default connection when <paramref name="connectionName"/> is <see langword="null"/>).</summary>
    /// <param name="connectionName">The connection name, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">Cancels acquiring the lease.</param>
    ValueTask<IConnectionLease> AcquireAsync(string? connectionName, CancellationToken cancellationToken = default);
}

/// <summary>
/// One use of a connection by one operation. Disposing it releases what the operation owns and nothing else.
/// </summary>
/// <remarks>
/// A lease from the pooled scope owns its connection and has no transaction. A lease borrowed from an ambient transaction or from
/// EF Core (R25) does not own the connection, so disposing it never closes, commits or rolls back anything.
/// </remarks>
public interface IConnectionLease : IAsyncDisposable
{
    /// <summary>The open connection to run commands on.</summary>
    DbConnection Connection { get; }

    /// <summary>The transaction every command must join (set <c>command.Transaction</c>), or <see langword="null"/> when there is none.</summary>
    DbTransaction? Transaction { get; }

    /// <summary><see langword="true"/> when disposing the lease closes <see cref="Connection"/>; <see langword="false"/> for a borrowed connection.</summary>
    bool OwnsConnection { get; }
}
