using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Queue.Jobs;
using Naravel.Queue.Serialization;

namespace Naravel.Queue.Extensions;

/// <summary>Registers queue jobs with dependency injection for worker-side deserialization.</summary>
/// <remarks><b>Laravel equivalent:</b> job class discovery for queue workers. It exists to give worker-only processes an explicit allow-list; implicit assembly scanning is available only when requested.</remarks>
public static class JobTypeRegistrationExtensions
{
    /// <summary>Registers a job with its explicit alias or its <see cref="JobAttribute"/> / full-name default.</summary>
    public static IServiceCollection AddJob<TJob>(this IServiceCollection services, string? alias = null)
        where TJob : IJob
    {
        services.Configure<JobTypeRegistryOptions>(options =>
            options.Registrations.Add(new JobRegistration(typeof(TJob), alias, null)));
        return services;
    }

    /// <summary>Registers a job with source-generated JSON metadata.</summary>
    public static IServiceCollection AddJob<TJob>(this IServiceCollection services, JsonTypeInfo<TJob> jsonTypeInfo, string? alias = null)
        where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        services.Configure<JobTypeRegistryOptions>(options =>
            options.Registrations.Add(new JobRegistration(typeof(TJob), alias, jsonTypeInfo)));
        return services;
    }

    /// <summary>Registers concrete job types in an assembly using their attribute aliases or full names.</summary>
    public static IServiceCollection AddJobsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var type in assembly.GetTypes().Where(type => typeof(IJob).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract))
            services.Configure<JobTypeRegistryOptions>(options => options.Registrations.Add(new JobRegistration(type, null, null)));
        return services;
    }
}