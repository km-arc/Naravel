using System.Text.Json.Serialization;

namespace Naravel.Mail.Queue;

/// <summary>Source-generated JSON metadata for <see cref="SendQueuedMailJob"/>, so queueing mail needs no reflection (trimming and Native AOT). Same options as the queue's default job serializer.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(SendQueuedMailJob))]
internal sealed partial class MailQueueJsonContext : JsonSerializerContext;
