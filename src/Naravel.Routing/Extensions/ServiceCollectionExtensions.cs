using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Routing;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registration of <c>Naravel.Routing</c>.</summary>
public static class NaravelRoutingServiceCollectionExtensions
{
    /// <summary>
    /// Registers routing services, <see cref="RoutingOptions"/>, <see cref="MiddlewarePipelineFactory"/> and <see cref="IUrlGenerator"/>.
    /// </summary>
    /// <remarks>
    /// <b>Laravel equivalent:</b> the <c>RoutingServiceProvider</c>. Routing is not driver-based, so it does not use
    /// <c>Manager&lt;TDriver,TOptions&gt;</c> (PDR-009).
    /// </remarks>
    public static IServiceCollection AddNaravelRouting(this IServiceCollection services, Action<RoutingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRouting();
        services.AddOptions<RoutingOptions>();
        if (configure is not null) services.Configure(configure);

        services.TryAddSingleton<MiddlewarePipelineFactory>();
        services.TryAddSingleton<IUrlGenerator, UrlGenerator>();
        return services;
    }
}
