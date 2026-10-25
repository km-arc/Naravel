using Microsoft.AspNetCore.Routing;
using Naravel.Broadcasting;

namespace Microsoft.AspNetCore.Builder;

/// <summary>Maps the broadcasting hub.</summary>
public static class BroadcastingEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps <see cref="BroadcastHub"/>, the one hub clients connect to. Put authentication and CORS on the returned builder as on any endpoint.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder, for example the <c>WebApplication</c>.</param>
    /// <param name="path">The hub path, for example <c>/hubs/broadcast</c>.</param>
    public static HubEndpointConventionBuilder MapNaravelBroadcasting(this IEndpointRouteBuilder endpoints, string path)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return endpoints.MapHub<BroadcastHub>(path);
    }
}
