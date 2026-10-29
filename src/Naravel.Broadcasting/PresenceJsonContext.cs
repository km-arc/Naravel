using System.Text.Json.Serialization;

namespace Naravel.Broadcasting;

/// <summary>Source-generated metadata for the presence records kept in the cache (same property names as before, so the stored format is unchanged).</summary>
[JsonSerializable(typeof(List<PresenceStore.Entry>))]
internal sealed partial class PresenceJsonContext : JsonSerializerContext;
