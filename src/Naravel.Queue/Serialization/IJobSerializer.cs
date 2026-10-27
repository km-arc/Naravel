using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Serialization;

/// <summary>Converts job instances to and from the payload and type alias stored in a queued message.</summary>
public interface IJobSerializer
{
    /// <summary>Gets the registered alias used to store this job type in a queue message.</summary>
    string GetTypeName(Type type);

    /// <summary>Resolves a registered queue-job alias without loading a type named by stored data.</summary>
    Type ResolveType(string typeName);

    /// <summary>Serializes a job payload using the configured JSON metadata.</summary>
    string Serialize(object job);

    /// <summary>Deserializes a payload into a type already resolved through the job registry.</summary>
    object Deserialize(string payload, Type type);
}

/// <summary>Default JSON-based serializer. Replace via DI (services.AddSingleton&lt;IJobSerializer&gt;(...)) for custom needs.</summary>
public class JsonJobSerializer : IJobSerializer
{
    private readonly IJobTypeRegistry _registry;
    private readonly JsonSerializerOptions _options;

    /// <summary>Initializes a new instance of the <see cref="JsonJobSerializer"/> class with a job type registry and optional JSON options.</summary>
    public JsonJobSerializer(IJobTypeRegistry registry, JsonSerializerOptions? options = null)
    {
        _registry = registry;
        _options = options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    /// <inheritdoc />
    public string GetTypeName(Type type) => _registry.GetAlias(type);

    /// <inheritdoc />
    public Type ResolveType(string typeName) => _registry.Resolve(typeName);

    /// <inheritdoc />
    public string Serialize(object job)
    {
        var typeInfo = _registry.GetJsonTypeInfo(job.GetType());
        return typeInfo is null
            ? JsonSerializer.Serialize(job, job.GetType(), _options)
            : JsonSerializer.Serialize(job, typeInfo);
    }

    /// <inheritdoc />
    public object Deserialize(string payload, Type type)
    {
        var typeInfo = _registry.GetJsonTypeInfo(type);
        return (typeInfo is null
            ? JsonSerializer.Deserialize(payload, type, _options)
            : JsonSerializer.Deserialize(payload, typeInfo))
            ?? throw new InvalidOperationException($"Failed to deserialize job payload for type '{type}'.");
    }
}
