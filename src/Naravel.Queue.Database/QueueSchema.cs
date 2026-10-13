using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Naravel.Queue.Database;

/// <summary>
/// Ready-made SQLite schema for the queue tables, plus an opt-in helper that applies it in development and test.
/// </summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>php artisan queue:table</c> / <c>queue:failed-table</c> / <c>queue:batches-table</c>,
/// which generate migrations.</para>
/// <para><b>Why it exists:</b> a quickstart or an integration test should not need to scaffold an EF migration just to
/// get a queue table. The script is plain SQL, so it can also be run with the <c>sqlite3</c> CLI.</para>
/// <para><b>Intentionally not ported:</b> automatic migration on startup. Nothing here runs unless the application calls
/// <see cref="EnsureSqliteSchemaAsync"/>, which refuses every environment except Development and Test. EF Core
/// migrations remain the production path for every provider (including SQLite).</para>
/// <para>The script assumes the UTC-ticks value converter on the <see cref="JobRecord"/> date columns, which SQLite
/// needs so the polling query can compare them (see <c>docs/en/queue.md</c>).</para>
/// </remarks>
public static class QueueSchema
{
    private const string SqliteProviderName = "Microsoft.EntityFrameworkCore.Sqlite";
    private const string SqliteResourceName = "Naravel.Queue.Database.Schema.sqlite.sql";

    private static readonly Lazy<string> Sqlite = new(LoadSqliteScript);

    /// <summary>The SQLite DDL for <c>QueueJobs</c>, <c>QueueFailedJobs</c> and <c>QueueBatches</c> (idempotent).</summary>
    public static string SqliteScript => Sqlite.Value;

    /// <summary>
    /// Creates the queue tables on a SQLite <paramref name="context"/> if they do not exist. Opt-in: call it yourself, for
    /// example from a development seed step or a test fixture.
    /// </summary>
    /// <param name="context">A context whose database provider is SQLite.</param>
    /// <param name="environment">Used to refuse anything other than Development, Test or Testing.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <exception cref="InvalidOperationException">The environment is not Development/Test, or the provider is not SQLite.</exception>
    public static async Task EnsureSqliteSchemaAsync(
        DbContext context,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment() &&
            !string.Equals(environment.EnvironmentName, "Test", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"QueueSchema.EnsureSqliteSchemaAsync only runs in Development or Test, but the environment is '{environment.EnvironmentName}'. " +
                "Use EF Core migrations to create the queue tables in other environments.");
        }

        if (!string.Equals(context.Database.ProviderName, SqliteProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"QueueSchema.EnsureSqliteSchemaAsync requires the SQLite provider, but the context uses '{context.Database.ProviderName}'.");
        }

        await context.Database.ExecuteSqlRawAsync(SqliteScript, cancellationToken);
    }

    private static string LoadSqliteScript()
    {
        using var stream = typeof(QueueSchema).Assembly.GetManifestResourceStream(SqliteResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{SqliteResourceName}' is missing from {typeof(QueueSchema).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
