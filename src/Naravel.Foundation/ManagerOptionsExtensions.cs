using Microsoft.Extensions.Configuration;

namespace Naravel.Foundation;

/// <summary>Helpers for module authors reading store configuration inside driver factories.</summary>
public static class ManagerOptionsExtensions
{
    /// <summary>
    /// Returns the configuration section of the named store, or throws a helpful error listing the configured stores.
    /// </summary>
    /// <param name="options">The module options.</param>
    /// <param name="name">Store name (case-insensitive).</param>
    /// <returns>The store's section; read values with the indexer or bind it to a typed class.</returns>
    /// <exception cref="InvalidOperationException">The store is not configured.</exception>
    public static IConfigurationSection GetStore(this IManagerOptions options, string name)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Stores.TryGetValue(name, out var section) && section is not null)
        {
            return section;
        }

        var configured = options.Stores.Count == 0 ? "(none)" : string.Join(", ", options.Stores.Keys.Order());
        throw new InvalidOperationException($"Store '{name}' is not configured. Configured stores: {configured}.");
    }
}
