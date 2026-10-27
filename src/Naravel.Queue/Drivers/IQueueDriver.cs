using Microsoft.Extensions.Configuration;

namespace Naravel.Queue.Drivers;

/// <summary>
/// The contract every queue backend (Memory, File, Redis, Database, RabbitMQ, Kafka, ...) must implement.
/// A driver only deals with opaque <see cref="QueuedMessage"/> envelopes - no knowledge of job types.
/// </summary>
public interface IQueueDriver
{
    /// <summary>Maximum reservation lifetime before a driver reclaims an unacknowledged message, if applicable.</summary>
    TimeSpan? VisibilityTimeout => null;

    /// <summary>
    /// Whether the backend can hold a message back until <see cref="QueuedMessage.AvailableAt"/> without delaying other
    /// messages (PDR-012a). Defaults to <c>true</c>. A driver that cannot (Kafka) returns <c>false</c>, rejects a delayed
    /// <see cref="PushAsync"/> and delivers a <see cref="ReleaseAsync"/> retry immediately; the worker then logs that the
    /// job's backoff is not applied.
    /// </summary>
    bool SupportsDelayedDelivery => true;

    /// <summary>Enqueue a new message (or a retried one) respecting its AvailableAt/Priority.</summary>
    Task PushAsync(QueuedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Atomically reserve and return the next available message for the given queue, or null if empty.</summary>
    Task<QueuedMessage?> PopAsync(string queue, CancellationToken cancellationToken = default);

    /// <summary>Permanently remove a message after it has been processed successfully.</summary>
    Task AckAsync(QueuedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Return a reserved message back to the queue after a failed attempt, to be retried after the given delay.</summary>
    Task ReleaseAsync(QueuedMessage message, TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>Move a message to the "failed" store after it has exhausted all attempts.</summary>
    Task FailAsync(QueuedMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="FailAsync"/>, but reports whether the call took effect. Drivers that fence acknowledgements to a
    /// reservation (Redis, File, Database) return <see langword="false"/> when the caller's reservation was reclaimed or
    /// taken over, so nothing was changed; the worker uses this to avoid recording a failure for a job that another
    /// worker now owns. The default implementation calls <see cref="FailAsync"/> and returns <see langword="true"/>.
    /// </summary>
    async Task<bool> TryFailAsync(QueuedMessage message, CancellationToken cancellationToken = default)
    {
        await FailAsync(message, cancellationToken);
        return true;
    }

    /// <summary>Approximate number of pending (ready + delayed) messages in a queue. Some drivers (e.g. Kafka) may return -1 if unavailable.</summary>
    Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default);
}

/// <summary>Small helpers shared by driver factories when reading their store's config section.</summary>
public static class ConfigurationSectionExtensions
{
    /// <summary>Returns the non-blank setting <paramref name="key"/> from the section and throws <see cref="InvalidOperationException"/> when it is missing.</summary>
    public static string GetRequired(this IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Naravel.Queue store '{section.Path}' is missing required setting '{key}'.");
        return value;
    }

    /// <summary>Returns the setting <paramref name="key"/> from the section, or <paramref name="defaultValue"/> when it is missing or blank.</summary>
    public static string GetOrDefault(this IConfigurationSection section, string key, string defaultValue)
        => string.IsNullOrWhiteSpace(section[key]) ? defaultValue : section[key]!;
}
