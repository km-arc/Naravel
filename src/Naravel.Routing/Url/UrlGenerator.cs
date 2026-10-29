using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Naravel.Routing;

internal sealed class UrlGenerator(LinkGenerator links, SignedUrlService signedUrls) : IUrlGenerator
{
    [RequiresUnreferencedCode("Reads the route values from the properties of an object with reflection. For trimming or Native AOT build the URL from a RouteValueDictionary through LinkGenerator.")]
    [RequiresDynamicCode("Reads the route values from the properties of an object with reflection.")]
    public string Route(string name, object? values = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return links.GetPathByName(name, values) ?? throw new RouteNotFoundException(name);
    }

    [RequiresUnreferencedCode("Reads the route values from the properties of an object with reflection. For trimming or Native AOT build the URL from a RouteValueDictionary through LinkGenerator.")]
    [RequiresDynamicCode("Reads the route values from the properties of an object with reflection.")]
    public string AbsoluteRoute(HttpContext context, string name, object? values = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return links.GetUriByName(context, name, values) ?? throw new RouteNotFoundException(name);
    }

    [RequiresUnreferencedCode("Reads the route values from the properties of an object with reflection. For trimming or Native AOT build the URL from a RouteValueDictionary through LinkGenerator.")]
    [RequiresDynamicCode("Reads the route values from the properties of an object with reflection.")]
    public string SignedRoute(string name, object? values = null) => signedUrls.Sign(Route(name, values), null);

    [RequiresUnreferencedCode("Reads the route values from the properties of an object with reflection. For trimming or Native AOT build the URL from a RouteValueDictionary through LinkGenerator.")]
    [RequiresDynamicCode("Reads the route values from the properties of an object with reflection.")]
    public string TemporarySignedRoute(string name, DateTimeOffset expiresAt, object? values = null) =>
        signedUrls.Sign(Route(name, values), expiresAt);
}
