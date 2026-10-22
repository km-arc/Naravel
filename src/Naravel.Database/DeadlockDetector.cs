using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Naravel.Database;

/// <summary>
/// Decides whether an exception is a deadlock or lock conflict worth retrying (Laravel: <c>DetectsLostConnections</c>/<c>DetectsConcurrencyErrors</c>).
/// Core must not reference provider packages, so SQL Server and SQLite are recognised by exception type name and numeric code, on the error path only.
/// </summary>
internal static class DeadlockDetector
{
    public static bool IsDeadlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            // 40001 = serialization failure / MySQL deadlock (1213), 40P01 = PostgreSQL deadlock_detected.
            if (current is DbException { SqlState: "40001" or "40P01" }) return true;
            if (IsProviderLockError(current)) return true;
        }

        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Reads a public property of a provider exception on the error path only; a trimmed-away property just means 'not a deadlock'.")]
    private static bool IsProviderLockError(Exception exception)
    {
        var name = exception.GetType().Name;
        if (name == "SqlException") return ReadInt(exception, "Number") == 1205;
        if (name == "SqliteException") return ReadInt(exception, "SqliteErrorCode") is 5 or 6; // SQLITE_BUSY, SQLITE_LOCKED
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "See IsProviderLockError.")]
    private static int ReadInt(Exception exception, string property) =>
        exception.GetType().GetProperty(property)?.GetValue(exception) is int value ? value : -1;
}
