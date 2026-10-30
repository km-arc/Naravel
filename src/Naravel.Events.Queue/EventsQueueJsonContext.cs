using System.Text.Json.Serialization;

namespace Naravel.Events.Queue;

/// <summary>Source-generated JSON metadata for <see cref="QueuedEventListenerJob"/>, so queued listeners need no reflection for the job itself (trimming and Native AOT). Same options as the queue's default job serializer.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(QueuedEventListenerJob))]
internal sealed partial class EventsQueueJsonContext : JsonSerializerContext;
