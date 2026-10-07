using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;

namespace Naravel.Routing;

internal sealed class SignedUrlService
{
    private const string SignatureKey = "signature";
    private const string ExpirationKey = "expires";
    private readonly IDataProtector _protector;

    public SignedUrlService(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("Naravel.Routing.SignedUrls.v1");

    public string Sign(string url, DateTimeOffset? expiresAt)
    {
        var (path, query) = SplitUrl(url);
        var values = QueryHelpers.ParseQuery(query);
        if (values.ContainsKey(SignatureKey) || values.ContainsKey(ExpirationKey))
        {
            throw new ArgumentException("Signed route values cannot contain reserved 'signature' or 'expires' query keys.", nameof(url));
        }

        if (expiresAt.HasValue)
        {
            values[ExpirationKey] = new StringValues(expiresAt.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        var canonical = Canonicalize(path, values);
        values[SignatureKey] = new StringValues(_protector.Protect(canonical));
        return Canonicalize(path, values);
    }

    public bool IsValid(HttpContext context)
    {
        var query = context.Request.Query;
        var signatures = query[SignatureKey];
        if (signatures.Count != 1 || string.IsNullOrWhiteSpace(signatures[0])) return false;

        var expirations = query[ExpirationKey];
        if (expirations.Count > 1) return false;
        if (expirations.Count == 1 &&
            (!long.TryParse(expirations[0], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt) ||
             DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt))
        {
            return false;
        }

        var values = query.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        values.Remove(SignatureKey);
        var path = context.Request.PathBase.Add(context.Request.Path).Value ?? "/";
        var canonical = Canonicalize(path, values);
        try
        {
            var protectedValue = _protector.Unprotect(signatures[0]!);
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(canonical), Encoding.UTF8.GetBytes(protectedValue));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static (string Path, string Query) SplitUrl(string url)
    {
        var separator = url.IndexOf('?');
        return separator < 0 ? (url, string.Empty) : (url[..separator], url[(separator + 1)..]);
    }

    private static string Canonicalize(string path, IEnumerable<KeyValuePair<string, StringValues>> values)
    {
        var builder = new QueryBuilder();
        foreach (var pair in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var value in pair.Value)
            {
                builder.Add(pair.Key, value ?? string.Empty);
            }
        }

        return path + builder.ToQueryString().ToUriComponent();
    }
}