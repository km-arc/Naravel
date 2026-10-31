using System.Text.Json;
using System.Text.Json.Serialization;
using Naravel.Queue.Batching;
using Naravel.Queue.Dispatch;
using Naravel.Queue.Drivers;

namespace Naravel.Queue.Serialization;

/// <summary>
/// JSON for the queue envelope and the parts the queue stores next to it (batch callbacks, chain links), with
/// source-generated metadata, so reading and writing them needs no reflection (trimming and Native AOT).
/// </summary>
/// <remarks>
/// <para><b>Why it exists:</b> every driver stores the same <see cref="QueuedMessage"/> envelope; one shared, compile-time
/// description keeps the stored shape identical across drivers and avoids a reflection based <c>JsonSerializer</c> call in
/// each of them. The output is the same JSON the default <c>JsonSerializer</c> produced before (property names as declared,
/// no naming policy), so envelopes already in a queue stay readable.</para>
/// <para>The job payload inside the envelope is a separate concern: <see cref="IJobSerializer"/> serialises it.</para>
/// </remarks>
public static class QueueEnvelopeJson
{
    /// <summary>Serializes an envelope to JSON text.</summary>
    /// <param name="message">The envelope.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(QueuedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.Serialize(message, QueueJsonContext.Default.QueuedMessage);
    }

    /// <summary>Serializes an envelope to UTF-8 JSON bytes.</summary>
    /// <param name="message">The envelope.</param>
    /// <returns>The UTF-8 JSON bytes.</returns>
    public static byte[] SerializeToUtf8Bytes(QueuedMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, QueueJsonContext.Default.QueuedMessage);
    }

    /// <summary>Reads an envelope from JSON text.</summary>
    /// <param name="json">The JSON text written by <see cref="Serialize(QueuedMessage)"/>.</param>
    /// <returns>The envelope, or <see langword="null"/> when the text is the JSON literal <c>null</c>.</returns>
    public static QueuedMessage? Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, QueueJsonContext.Default.QueuedMessage);
    }

    /// <summary>Creates an independent copy of an envelope.</summary>
    /// <param name="message">The envelope to copy.</param>
    /// <returns>A copy with the same values.</returns>
    public static QueuedMessage Clone(QueuedMessage message)
        => Deserialize(Serialize(message)) ?? throw new InvalidOperationException("A queue envelope could not be copied.");

    /// <summary>Serializes the callbacks registered for a batch to JSON text.</summary>
    /// <param name="callbacks">The callback references.</param>
    /// <returns>The JSON text, a JSON array.</returns>
    public static string SerializeCallbacks(IReadOnlyList<BatchCallbackReference> callbacks)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        return JsonSerializer.Serialize(callbacks, QueueJsonContext.Default.CallbackReferencesReadOnly);
    }

    /// <summary>Reads the callbacks of a batch from JSON text.</summary>
    /// <param name="json">The JSON text written by <see cref="SerializeCallbacks(IReadOnlyList{BatchCallbackReference})"/>.</param>
    /// <returns>The callback references; empty when the text is the JSON literal <c>null</c>.</returns>
    public static List<BatchCallbackReference> DeserializeCallbacks(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, QueueJsonContext.Default.CallbackReferences) ?? new List<BatchCallbackReference>();
    }
}

/// <summary>Source-generated metadata for the queue envelope, batch callbacks and chain links.</summary>
[JsonSerializable(typeof(QueuedMessage))]
[JsonSerializable(typeof(List<ChainLink>), TypeInfoPropertyName = "ChainLinks")]
[JsonSerializable(typeof(IReadOnlyList<BatchCallbackReference>), TypeInfoPropertyName = "CallbackReferencesReadOnly")]
[JsonSerializable(typeof(List<BatchCallbackReference>), TypeInfoPropertyName = "CallbackReferences")]
internal sealed partial class QueueJsonContext : JsonSerializerContext;
