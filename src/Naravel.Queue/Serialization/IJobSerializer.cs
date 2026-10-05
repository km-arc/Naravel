using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Serialization;

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

    public JsonJobSerializer(IJobTypeRegistry registry, JsonSerializerOptions? options = null)
    {
        _registry = registry;
        _options = options ?? new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    public string GetTypeName(Type type) => _registry.GetAlias(type);

    public Type ResolveType(string typeName) => _registry.Resolve(typeName);

    public string Serialize(object job)
    {
        var typeInfo = _registry.GetJsonTypeInfo(job.GetType());
        return typeInfo is null
            ? JsonSerializer.Serialize(job, job.GetType(), _options)
            : JsonSerializer.Serialize(job, typeInfo);
    }

    public object Deserialize(string payload, Type type)
    {
        var typeInfo = _registry.GetJsonTypeInfo(type);
        return (typeInfo is null
            ? JsonSerializer.Deserialize(payload, type, _options)
            : JsonSerializer.Deserialize(payload, typeInfo))
            ?? throw new InvalidOperationException($"Failed to deserialize job payload for type '{type}'.");
    }
}
