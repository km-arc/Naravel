using System.Text.Json.Serialization;

namespace Naravel.Notifications.Queue;

/// <summary>Source-generated JSON metadata for <see cref="SendQueuedNotificationJob"/>, so queueing notifications needs no reflection (trimming and Native AOT). Same options as the queue's default job serializer.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SendQueuedNotificationJob))]
internal sealed partial class NotificationQueueJsonContext : JsonSerializerContext;
