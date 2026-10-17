using System.Text.RegularExpressions;

namespace Naravel.Database;

/// <summary>An identifier that passed the frozen grammar: one to three name segments, an optional trailing <c>*</c> and an optional alias.</summary>
internal sealed record ParsedIdentifier(IReadOnlyList<string> Segments, bool IsStar, string? Alias);

/// <summary>
/// The identifier grammar frozen by PDR-022 A3. Relaxing it needs a PDR amendment, never a local change.
/// </summary>
/// <remarks>
/// Segment: <c>^[\p{L}_][\p{L}\p{N}_$]*\z</c>, at most 128 characters (the end anchor is <c>\z</c>, not <c>$</c>, because <c>$</c>
/// would accept a trailing newline). Forms: <c>col</c>, <c>table.col</c>, <c>schema.table.col</c>, <c>*</c> and <c>table.*</c>,
/// <c>source as alias</c>. JSON paths (<c>col-&gt;key</c>) are recognised so they are rejected with a precise message until the
/// JSON where methods exist.
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
            if (!IsBase(source[..arrow], allowStar: false, out _) || path.Any(part => part.Length == 0 || part.Length > MaxSegmentLength || !PathSegmentPattern.IsMatch(part)))
            {
                throw Invalid(text, parameterName);
            }

            throw new NotSupportedException("JSON column paths ('col->key') are not supported yet; their syntax is frozen by PDR-022 A3 and they arrive with the JSON where methods.");
        }

        if (!IsBase(source, allowStar: true, out var segments)) throw Invalid(text, parameterName);
        var star = segments[^1] == "*";
        if (star && alias is not null) throw Invalid(text, parameterName);
        return new ParsedIdentifier(segments, star, alias);
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
