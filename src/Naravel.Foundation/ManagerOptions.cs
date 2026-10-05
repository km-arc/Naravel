using Microsoft.Extensions.Configuration;

namespace Naravel.Foundation;

/// <summary>
/// Ready-to-bind base class for module options (for example <c>CacheOptions : ManagerOptions</c>).
/// </summary>
/// <remarks>
/// Binds from a section such as
/// <code>
/// { "Cache": { "Default": "redis", "Stores": { "redis": { "Host": "localhost" }, "file": { "Path": "cache" } } } }
/// </code>
/// Store names are case-insensitive.
/// </remarks>
public class ManagerOptions : IManagerOptions
{
    /// <summary>Name of the default store. Empty means "not configured".</summary>
    public string Default { get; set; } = string.Empty;

    /// <summary>Configured stores keyed by name (case-insensitive).</summary>
    public Dictionary<string, IConfigurationSection> Stores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    IDictionary<string, IConfigurationSection> IManagerOptions.Stores => Stores;
}
