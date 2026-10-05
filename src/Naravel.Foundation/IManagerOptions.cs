using Microsoft.Extensions.Configuration;

namespace Naravel.Foundation;

/// <summary>
/// The configuration shape every Naravel manager understands: a default driver name and a dictionary of
/// named stores. Mirrors Laravel's <c>config/cache.php</c> (<c>'default'</c> + <c>'stores'</c>).
/// </summary>
/// <remarks>
/// <para><b>Why raw <see cref="IConfigurationSection"/> values:</b> the manager must not know what a Redis or
/// file store needs. Each driver factory binds its own typed options from its store section, which keeps modules
/// decoupled (Laravel's flat arrays are untyped and easy to typo).</para>
/// <para><b>Hot reload:</b> sections are live views of the configuration, so reloaded values are visible to
/// factories without extra work; the manager decides when to rebuild drivers (see PDR-004).</para>
/// </remarks>
public interface IManagerOptions
{
    /// <summary>Name of the store used when no name is passed to the manager.</summary>
    string Default { get; }

    /// <summary>Configured stores keyed by name (case-insensitive).</summary>
    IDictionary<string, IConfigurationSection> Stores { get; }
}
