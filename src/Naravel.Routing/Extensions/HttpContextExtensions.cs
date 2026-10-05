using Naravel.Routing;

namespace Microsoft.AspNetCore.Http;

/// <summary>Access to route-model-binding results.</summary>
public static class NaravelRoutingHttpContextExtensions
{
    /// <summary>
    /// Returns the model that the <c>bindings</c> middleware resolved for a route parameter, or <c>null</c> when there is none.
    /// </summary>
    public static TModel? GetRouteModel<TModel>(this HttpContext context, string parameter) where TModel : class
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(RouteBindingKeys.For(parameter), out var model) ? model as TModel : null;
    }
}
