namespace Naravel.Database;

/// <summary>
/// A piece of SQL text with its own bound values, used <b>as a value</b> where a query takes one: <c>Where("votes", "&gt;", new Raw("avg_votes + ?", 1))</c>, an update or insert value
/// (<c>["votes"] = new Raw("votes + ?", 1)</c>), a <c>between</c> bound. Laravel: <c>DB::raw</c>.
/// </summary>
/// <remarks>
/// <para>This is the one way SQL text enters a query outside the <c>*Raw</c> methods (<c>SelectRaw</c>, <c>WhereRaw</c>, <c>OrWhereRaw</c>, <c>HavingRaw</c>, <c>OrHavingRaw</c>, <c>OrderByRaw</c>, <c>GroupByRaw</c>, <c>FromRaw</c>): every other string a
/// query method takes is a value (bound as a parameter) or an identifier (validated and quoted). <b>Never build the text from user input</b>; pass user values as bindings: each <c>?</c> is replaced by one bound
/// value in order, and <c>??</c> writes a literal question mark.</para>
/// <para>The text is not translated between dialects: write it for the database the query runs on. A raw value in a batch insert counts as one parameter when the batch is split, so a raw value with several
/// bindings can make a statement pass the dialect's limit; that is reported as an error.</para>
/// </remarks>
public sealed class Raw
{
    /// <summary>Creates a raw expression.</summary>
    /// <param name="sql">The SQL text; each <c>?</c> is one binding.</param>
    /// <param name="bindings">The values, one per <c>?</c>; they are always sent as parameters.</param>
    /// <exception cref="ArgumentException">The text is empty, or the number of <c>?</c> placeholders differs from the number of bindings.</exception>
    public Raw(string sql, params object?[] bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ArgumentNullException.ThrowIfNull(bindings);
        if (Grammar.CountPlaceholders(sql) != bindings.Length)
        {
            throw new ArgumentException($"The raw SQL has {Grammar.CountPlaceholders(sql)} '?' placeholders but {bindings.Length} bindings were given. Write '??' for a literal question mark.", nameof(bindings));
        }

        Sql = sql;
        Bindings = bindings.ToArray();
    }

    /// <summary>The SQL text.</summary>
    public string Sql { get; }

    /// <summary>The values bound to the <c>?</c> placeholders, in order.</summary>
    public IReadOnlyList<object?> Bindings { get; }
}
