using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Naravel.Routing;

internal sealed class UrlGenerator(LinkGenerator links) : IUrlGenerator
{
    public string Route(string name, object? values = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return links.GetPathByName(name, values) ?? throw new RouteNotFoundException(name);
    }

    public string AbsoluteRoute(HttpContext context, string name, object? values = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return links.GetUriByName(context, name, values) ?? throw new RouteNotFoundException(name);
    }
}
