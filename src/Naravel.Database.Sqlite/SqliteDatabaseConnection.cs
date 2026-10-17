using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Naravel.Database.Sqlite;

/// <summary>A named SQLite connection definition (<c>Driver: sqlite</c>).</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>Illuminate\Database\SQLiteConnection</c>.</para>
/// <para><b>Why it exists:</b> opens pooled <see cref="SqliteConnection"/>s for the query builder. <b>In-memory databases:</b> SQLite
/// deletes an in-memory database when its last connection closes, and the builder opens one connection per operation. So for
/// <c>Data Source=:memory:</c> (or <c>Mode=Memory</c>) the driver switches to a uniquely named shared-cache memory database and keeps
/// one connection open until the driver is disposed; every operation then sees the same data, and two connection names never share one.</para>
/// </remarks>
public sealed class SqliteDatabaseConnection : IDatabaseConnection, IDisposable, IAsyncDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _keeper;
    private bool _disposed;

    /// <summary>Creates the definition from a Microsoft.Data.Sqlite connection string.</summary>
    /// <param name="name">The connection name.</param>
    /// <param name="connectionString">The connection string, for example <c>Data Source=app.db</c> or <c>Data Source=:memory:</c>.</param>
    public SqliteDatabaseConnection(string name, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Name = name;
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var privateMemory = string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal);
        var memory = privateMemory || builder.Mode == SqliteOpenMode.Memory;
        if (privateMemory)
        {
            builder.DataSource = $"naravel-{name}-{Guid.NewGuid():N}";
            builder.Mode = SqliteOpenMode.Memory;
            builder.Cache = SqliteCacheMode.Shared;
        }

        _connectionString = builder.ToString();
        if (memory)
        {
            _keeper = new SqliteConnection(_connectionString);
            _keeper.Open();
        }
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public DatabaseDialect Dialect => DatabaseDialect.Sqlite;

    /// <inheritdoc />
    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return connection;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _keeper, null)?.Dispose();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        var keeper = Interlocked.Exchange(ref _keeper, null);
        return keeper is null ? ValueTask.CompletedTask : keeper.DisposeAsync();
    }
}
