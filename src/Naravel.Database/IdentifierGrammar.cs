using System.Text.RegularExpressions;

namespace Naravel.Database;

/// <summary>An identifier that passed the frozen grammar: one to three name segments, an optional trailing <c>*</c> and an optional alias.</summary>
internal sealed record ParsedIdentifier(IReadOnlyList<string> Segments, bool IsStar, string? Alias, IReadOnlyList<string>? JsonPath = null);

/// <summary>
/// The identifier grammar frozen by PDR-022 A3. Relaxing it needs a PDR amendment, never a local change.
/// </summary>
/// <remarks>
/// Segment: <c>^[\p{L}_][\p{L}\p{N}_$]*\z</c>, at most 128 characters (the end anchor is <c>\z</c>, not <c>$</c>, because <c>$</c>
/// would accept a trailing newline). Forms: <c>col</c>, <c>table.col</c>, <c>schema.table.col</c>, <c>*</c> and <c>table.*</c>,
/// <c>source as alias</c>. JSON paths (<c>col-&gt;key</c>) are parsed into <see cref="ParsedIdentifier.JsonPath"/>; only where clauses accept them.
/// </remarks>
internal static class IdentifierGrammar
{
    internal const int MaxSegmentLength = 128;

    private static readonly Regex SegmentPattern = new(@"^[\p{L}_][\p{L}\p{N}_$]*\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex PathSegmentPattern = new(@"^[\p{L}\p{N}_]+(\[\d+\])?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static ParsedIdentifier Parse(string text, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(text, parameterName);

        var source = text;
        string? alias = null;
        var aliasIndex = text.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (aliasIndex >= 0)
        {
            if (text.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase) != aliasIndex) throw Invalid(text, parameterName);
            source = text[..aliasIndex];
            alias = text[(aliasIndex + 4)..];
            if (!IsSegment(alias)) throw Invalid(text, parameterName);
        }

        var arrow = source.IndexOf("->", StringComparison.Ordinal);
        if (arrow >= 0)
        {
            var path = source[(arrow + 2)..].Split("->");
            if (alias is not null || !IsBase(source[..arrow], allowStar: false, out var baseSegments) || path.Any(part => part.Length == 0 || part.Length > MaxSegmentLength || !PathSegmentPattern.IsMatch(part)))
            {
                throw Invalid(text, parameterName);
            }

            return new ParsedIdentifier(baseSegments, false, null, path);
        }

        if (!IsBase(source, allowStar: true, out var segments)) throw Invalid(text, parameterName);
        var star = segments[^1] == "*";
        if (star && alias is not null) throw Invalid(text, parameterName);
        return new ParsedIdentifier(segments, star, alias);
    }

    /// <summary>The JSON path text <c>$."a"."b"[0]</c> understood by SQLite, MySQL, SQL Server and PostgreSQL (jsonpath).</summary>
    internal static string JsonPathText(IReadOnlyList<string> path)
    {
        var text = new System.Text.StringBuilder("$");
        foreach (var part in path)
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            text.Append(".\"").Append(bracket < 0 ? part : part[..bracket]).Append('"');
            if (bracket >= 0) text.Append(part.AsSpan(bracket));
        }

        return text.ToString();
    }

    /// <summary>The PostgreSQL text-array literal <c>{a,b,0}</c> for the <c>#&gt;</c> and <c>#&gt;&gt;</c> operators.</summary>
    internal static string JsonPathArray(IReadOnlyList<string> path)
    {
        var items = new List<string>();
        foreach (var part in path)
        {
            var bracket = part.IndexOf('[', StringComparison.Ordinal);
            items.Add(bracket < 0 ? part : part[..bracket]);
            if (bracket >= 0) items.Add(part[(bracket + 1)..^1]);
        }

        return "{" + string.Join(',', items) + "}";
    }

    private static bool IsBase(string text, bool allowStar, out string[] segments)
    {
        segments = text.Split('.');
        if (segments.Length is < 1 or > 3) return false;
        for (var i = 0; i < segments.Length; i++)
        {
            if (allowStar && i == segments.Length - 1 && segments[i] == "*") continue;
            if (!IsSegment(segments[i])) return false;
        }

        return true;
    }

    private static bool IsSegment(string value) => value.Length is > 0 and <= MaxSegmentLength && SegmentPattern.IsMatch(value);

    internal static ArgumentException Invalid(string text, string parameterName) =>
        new($"'{Truncate(text)}' is not a valid identifier. Use letters, digits and underscores in 'name', 'table.name', 'schema.table.name' or 'source as alias'; use a Raw method for anything else.", parameterName);

    internal static string Truncate(string text) => text.Length <= 64 ? text : text[..64] + "...";
}
