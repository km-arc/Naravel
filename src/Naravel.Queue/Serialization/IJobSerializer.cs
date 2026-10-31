using System.Diagnostics.CodeAnalysis;
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
            ? ReflectionSerialize(job)
            : JsonSerializer.Serialize(job, typeInfo);
    }

    /// <inheritdoc />
    public object Deserialize(string payload, Type type)
    {
        var typeInfo = _registry.GetJsonTypeInfo(type);
        return (typeInfo is null
            ? ReflectionDeserialize(payload, type)
            : JsonSerializer.Deserialize(payload, typeInfo))
            ?? throw new InvalidOperationException($"Failed to deserialize job payload for type '{type}'.");
    }

    // The two reflection based calls below are reached only for a job registered without JsonTypeInfo. The registrations
    // that do that (AddJob<TJob>(alias), AddJobsFromAssembly) carry RequiresUnreferencedCode and RequiresDynamicCode, so the
    // warning is raised where the job is registered. With Native AOT, System.Text.Json reflection is disabled and these
    // calls throw instead of losing data.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only jobs registered through the annotated reflection registrations reach this path.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only jobs registered through the annotated reflection registrations reach this path; Native AOT disables it and System.Text.Json throws.")]
    private string ReflectionSerialize(object job) => JsonSerializer.Serialize(job, job.GetType(), _options);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only jobs registered through the annotated reflection registrations reach this path.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Only jobs registered through the annotated reflection registrations reach this path; Native AOT disables it and System.Text.Json throws.")]
    private object? ReflectionDeserialize(string payload, Type type) => JsonSerializer.Deserialize(payload, type, _options);
}
