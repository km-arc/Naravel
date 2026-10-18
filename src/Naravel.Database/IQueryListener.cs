namespace Naravel.Database;

/// <summary>Receives a <see cref="QueryExecuted"/> after every command, successful or not. Register implementations in DI.</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> <c>DB::listen(function (QueryExecuted $query) { ... })</c>.</para>
/// <para><b>Why it exists:</b> a hook for logging, slow-query detection and tests, without wrapping the connection. Register with
/// <c>services.AddSingleton&lt;IQueryListener, MyListener&gt;()</c>; the listeners are resolved once, when <see cref="IDatabase"/> is first resolved.</para>
/// <para>Listeners run synchronously on the calling thread, so keep them fast. An exception thrown by a listener is swallowed so that a broken
/// logger can never fail a query or hide the query's own exception.</para>
/// </remarks>
public interface IQueryListener
{
    /// <summary>Called after a command ran.</summary>
    /// <param name="query">The command, its values, its duration and its failure if any.</param>
    void OnQueryExecuted(QueryExecuted query);
}
