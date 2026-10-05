using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Naravel.Queue.Jobs;

namespace Naravel.Queue.Serialization;

/// <summary>Stable alias registry used to resolve queued job types without trusting stored CLR type names.</summary>
/// <remarks><b>Laravel equivalent:</b> queue job serialization identity. It exists to prevent stored data from selecting arbitrary CLR types; assembly-qualified names and reflection-based resolution are intentionally not supported.</remarks>
public interface IJobTypeRegistry
{
    /// <summary>Registers a concrete job type under an optional stable alias.</summary>
    void Register(Type type, string? alias = null, JsonTypeInfo? jsonTypeInfo = null);

    /// <summary>Returns the registered alias for a job type.</summary>
    string GetAlias(Type type);

    /// <summary>Resolves an alias to a registered job type or throws <see cref="UnknownJobTypeException"/>.</summary>
    Type Resolve(string alias);

    /// <summary>Gets source-generated JSON metadata registered for a job type, if any.</summary>
    JsonTypeInfo? GetJsonTypeInfo(Type type);
}

/// <summary>Provides a stable queue identity for a job. The default alias is the type's full name.</summary>
/// <remarks><b>Laravel equivalent:</b> a queued job's serialized class identity. It exists so deployments can rename CLR types without invalidating queued jobs; PHP serialization mechanics are not ported.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobAttribute(string alias) : Attribute
{
    /// <summary>The stable alias written into queued messages.</summary>
    public string Alias { get; } = alias;
}

/// <summary>Raised when a queued message names a job alias that is not registered.</summary>
/// <remarks><b>Laravel equivalent:</b> an invalid queued job class. It exists to fail unknown messages intentionally before deserialization; arbitrary CLR type loading is not supported.</remarks>
public sealed class UnknownJobTypeException(string name)
    : Exception($"Queue job type alias '{name}' is not registered.")
{
    /// <summary>The unknown alias from the queued message.</summary>
    public string Name { get; } = name;
}

internal sealed class JobTypeRegistryOptions
{
    internal List<JobRegistration> Registrations { get; } = new();
}

internal sealed record JobRegistration(Type Type, string? Alias, JsonTypeInfo? JsonTypeInfo);

internal sealed class JobTypeRegistry : IJobTypeRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Type> _typesByAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _aliasesByType = new();
    private readonly Dictionary<Type, JsonTypeInfo> _jsonTypeInfoByType = new();

    public JobTypeRegistry(IOptions<JobTypeRegistryOptions> options)
    {
        foreach (var registration in options.Value.Registrations)
            Register(registration.Type, registration.Alias, registration.JsonTypeInfo);
    }

    public void Register(Type type, string? alias = null, JsonTypeInfo? jsonTypeInfo = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(IJob).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
            throw new ArgumentException($"'{type}' must be a concrete {nameof(IJob)} type.", nameof(type));
        if (jsonTypeInfo is not null && jsonTypeInfo.Type != type)
            throw new ArgumentException("The JSON type info must describe the registered job type.", nameof(jsonTypeInfo));

        var resolvedAlias = alias ?? type.GetCustomAttribute<JobAttribute>()?.Alias ?? type.FullName ?? type.Name;
        if (string.IsNullOrWhiteSpace(resolvedAlias))
            throw new ArgumentException("A job alias cannot be empty.", nameof(alias));

        lock (_sync)
        {
            if (_aliasesByType.TryGetValue(type, out var existingAlias) && alias is null)
            {
                if (jsonTypeInfo is not null) _jsonTypeInfoByType[type] = jsonTypeInfo;
                return;
            }

            if (_typesByAlias.TryGetValue(resolvedAlias, out var registeredType) && registeredType != type)
                throw new InvalidOperationException($"Queue job alias '{resolvedAlias}' is already registered for '{registeredType}'.");

            if (_aliasesByType.TryGetValue(type, out existingAlias) && existingAlias != resolvedAlias)
                _typesByAlias.Remove(existingAlias);

            _typesByAlias[resolvedAlias] = type;
            _aliasesByType[type] = resolvedAlias;
            if (jsonTypeInfo is not null) _jsonTypeInfoByType[type] = jsonTypeInfo;
        }
    }

    public string GetAlias(Type type)
    {
        lock (_sync)
            return _aliasesByType.TryGetValue(type, out var alias)
                ? alias
                : throw new UnknownJobTypeException(type.FullName ?? type.Name);
    }

    public Type Resolve(string alias)
    {
        lock (_sync)
            return _typesByAlias.TryGetValue(alias, out var type)
                ? type
                : throw new UnknownJobTypeException(alias);
    }

    public JsonTypeInfo? GetJsonTypeInfo(Type type)
    {
        lock (_sync)
            return _jsonTypeInfoByType.GetValueOrDefault(type);
    }
}
