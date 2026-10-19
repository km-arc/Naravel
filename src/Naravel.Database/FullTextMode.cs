namespace Naravel.Database;

/// <summary>How <see cref="Query.WhereFullText(string, string, FullTextMode)"/> interprets the search text.</summary>
public enum FullTextMode
{
    /// <summary>Plain words, ranked by relevance (<c>IN NATURAL LANGUAGE MODE</c>, <c>plainto_tsquery</c>, <c>FREETEXT</c>).</summary>
    Natural = 0,

    /// <summary>The engine's own boolean query syntax (<c>IN BOOLEAN MODE</c>, <c>to_tsquery</c>, <c>CONTAINS</c>); the text is passed to the engine as a bound value.</summary>
    Boolean = 1,
}
