namespace Naravel.Database;

/// <summary>The result of compiling a <see cref="Query"/>: SQL text with <c>@p0</c>, <c>@p1</c>... markers and the values to bind, in marker order.</summary>
/// <remarks><b>Laravel equivalent:</b> <c>toSql()</c> together with <c>getBindings()</c>.</remarks>
public sealed class CompiledQuery
{
    internal CompiledQuery(string sql, IReadOnlyList<object?> bindings)
    {
        Sql = sql;
        Bindings = bindings;
    }

    /// <summary>The SQL text. Values never appear in it; they are bound as parameters named <c>@p0</c>, <c>@p1</c>, and so on.</summary>
    public string Sql { get; }

    /// <summary>The values for <c>@p0</c>, <c>@p1</c>... in order.</summary>
    public IReadOnlyList<object?> Bindings { get; }
}
