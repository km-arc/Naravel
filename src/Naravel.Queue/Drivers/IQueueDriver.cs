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

    /// <summary>Approximate number of pending (ready + delayed) messages in a queue. Some drivers (e.g. Kafka) may return -1 if unavailable.</summary>
    Task<long> SizeAsync(string queue, CancellationToken cancellationToken = default);
}

/// <summary>Small helpers shared by driver factories when reading their store's config section.</summary>
public static class ConfigurationSectionExtensions
{
    public static string GetRequired(this IConfigurationSection section, string key)
    {
        var value = section[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"Naravel.Queue store '{section.Path}' is missing required setting '{key}'.");
        return value;
    }

    public static string GetOrDefault(this IConfigurationSection section, string key, string defaultValue)
        => string.IsNullOrWhiteSpace(section[key]) ? defaultValue : section[key]!;
}
