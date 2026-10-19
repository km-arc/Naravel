using System.Text;

namespace Naravel.Pagination;

/// <summary>Builds the page and cursor links. Query values are percent-encoded; the page or cursor parameter is always written last, after the caller's own values (Laravel's <c>withQueryString</c> order).</summary>
internal static class PageUrl
{
    public static string? Build(string? path, IReadOnlyList<KeyValuePair<string, string?>> query, string name, string value)
    {
        if (path is null) return null;

        var url = new StringBuilder(path);
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        foreach (var pair in query)
        {
            // The page parameter is replaced, never duplicated.
            if (string.Equals(pair.Key, name, StringComparison.Ordinal)) continue;
            url.Append(separator).Append(Uri.EscapeDataString(pair.Key)).Append('=').Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
            separator = '&';
        }

        return url.Append(separator).Append(Uri.EscapeDataString(name)).Append('=').Append(Uri.EscapeDataString(value)).ToString();
    }

    public static IReadOnlyList<KeyValuePair<string, string?>> Copy(IEnumerable<KeyValuePair<string, string?>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return query.ToArray();
    }

    public static string CheckName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    public static string? CheckPath(string? path)
    {
        if (path is not null) ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path;
    }
}
