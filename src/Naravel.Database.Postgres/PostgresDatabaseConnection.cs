using System.Data.Common;
using Npgsql;

namespace Naravel.Database.Postgres;

/// <summary>A named PostgreSQL connection definition (<c>Driver: pgsql</c>), built on Npgsql.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\PostgresConnection</c>.</para>
/// <para><b>Why it exists:</b> holds one <see cref="NpgsqlDataSource"/>, Npgsql's recommended pooled entry point, and opens a connection from it for each lease.
/// The data source is created without touching the network and is disposed with the driver (when the manager is disposed or the connection is rebuilt).</para>
/// </remarks>
public sealed class PostgresDatabaseConnection : IDatabaseConnection, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private bool _disposed;

    /// <summary>Creates the definition from an Npgsql connection string.</summary>
    /// <param name="name">The connection name.</param>
    /// <param name="connectionString">For example <c>Host=localhost;Database=app;Username=app;Password=...</c>.</param>
    public PostgresDatabaseConnection(string name, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Name = name;
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DatabaseDialect Dialect => DatabaseDialect.Postgres;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        _dataSource.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return _dataSource.DisposeAsync();
    }
}
