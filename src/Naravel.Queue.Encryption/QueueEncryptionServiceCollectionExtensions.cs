using Microsoft.Extensions.DependencyInjection.Extensions;
using Naravel.Queue.Encryption;
using Naravel.Queue.Serialization;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the optional encrypted-jobs adapter for <c>Naravel.Queue</c>.</summary>
public static class QueueEncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the registered <see cref="IJobSerializer"/> with a decorator that encrypts the payload of jobs marked <see cref="IShouldBeEncrypted"/>.
    /// Call it after <c>AddQueue</c>. It also registers Data Protection when the application has not (see <c>AddNaravelEncryption</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Laravel equivalent:</b> <c>ShouldBeEncrypted</c> queue jobs.</para>
    /// <para><b>Why it exists:</b> opt-in encryption of selected job payloads over Data Protection, keeping the alias registry the only way a type is chosen (PDR-021, OD-07).
    /// Unmarked jobs keep the existing wire format; chains, batches and failed jobs carry the encrypted string unchanged.</para>
    /// <para><b>Key ring:</b> the producer and every worker must share the Data Protection key ring and application name, or marked jobs fail with <see cref="Naravel.Encryption.DecryptException"/>.</para>
    /// <para><b>Not ported on purpose:</b> encrypting all jobs, and any plaintext fallback for a marked job (except the explicit migration option).</para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional options, e.g. <see cref="QueueEncryptionOptions.AcceptPlaintextForMarkedJobs"/>.</param>
    /// <exception cref="InvalidOperationException">No <see cref="IJobSerializer"/> is registered yet: call <c>AddQueue</c> first.</exception>
    public static IServiceCollection AddNaravelQueueEncryption(this IServiceCollection services, Action<QueueEncryptionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IJobSerializer))
            ?? throw new InvalidOperationException("No IJobSerializer is registered. Call AddQueue before AddNaravelQueueEncryption.");
        if (existing.ImplementationFactory?.Target is EncryptedFactory)
        {
            return services;
        }

        services.AddNaravelEncryption();
        var options = services.AddOptions<QueueEncryptionOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.Remove(existing);
        services.Add(ServiceDescriptor.Describe(typeof(IJobSerializer), new EncryptedFactory(existing).Create, existing.Lifetime));
        return services;
    }

    private sealed class EncryptedFactory(ServiceDescriptor inner)
    {
        public object Create(IServiceProvider provider)
        {
            var serializer = (IJobSerializer)Inner(provider);
            return ActivatorUtilities.CreateInstance<EncryptedJobSerializer>(provider, serializer);
        }

        private object Inner(IServiceProvider provider) =>
            inner.ImplementationInstance
            ?? inner.ImplementationFactory?.Invoke(provider)
            ?? ActivatorUtilities.CreateInstance(provider, inner.ImplementationType!);
    }
}
