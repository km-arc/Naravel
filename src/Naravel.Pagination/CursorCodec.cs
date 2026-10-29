using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Naravel.Pagination;

/// <summary>Which way a cursor points from the item it was made from.</summary>
internal enum CursorDirection
{
    /// <summary>The items after the position.</summary>
    Next,

    /// <summary>The items before the position.</summary>
    Previous,
}

/// <summary>The content of a decoded cursor.</summary>
internal sealed class CursorValues
{
    internal CursorValues(CursorDirection direction, IReadOnlyList<object> values)
    {
        Direction = direction;
        Values = values;
    }

    /// <summary>Whether the cursor asks for the items after or before its position.</summary>
    internal CursorDirection Direction { get; }

    /// <summary>The key values of the position, one per declared key, never <see langword="null"/>.</summary>
    internal IReadOnlyList<object> Values { get; }
}

/// <summary>Writes and reads the opaque cursor string (PDR-020).</summary>
/// <remarks>
/// <para><b>Laravel equivalent:</b> the cursor of <c>CursorPaginator</c>, which is base64 JSON as well.</para>
/// <para><b>What a cursor is:</b> a base64url string of <c>{"d":"n"|"p","k":[...]}</c>. It is a <b>position</b>, not a secret and not a permission, and it is not signed. Reading is strict so that a forged cursor can only move the position: it must be at most <see cref="MaxLength"/> characters, valid base64url, exactly that JSON, with one value per declared key, each value converted to the declared type, none <see langword="null"/>. The values are only ever bound as query parameters by the caller; column names never come from a cursor.</para>
/// </remarks>
internal static class CursorCodec
{
    /// <summary>The longest cursor that is accepted, in characters.</summary>
    internal const int MaxLength = 1024;

    /// <summary>Makes a cursor.</summary>
    /// <param name="direction">Which way the cursor points.</param>
    /// <param name="values">The key values of the boundary item, in key order.</param>
    /// <param name="types">The declared CLR type of each key, in the same order.</param>
    /// <exception cref="ArgumentException">The lists differ in length or are empty.</exception>
    /// <exception cref="InvalidOperationException">A value is <see langword="null"/>; null keys are not supported.</exception>
    internal static string Encode(CursorDirection direction, IReadOnlyList<object?> values, IReadOnlyList<Type> types)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(types);
        if (values.Count == 0 || values.Count != types.Count) throw new ArgumentException("A cursor needs one value per key, and at least one key.", nameof(values));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("d", direction == CursorDirection.Next ? "n" : "p");
            writer.WriteStartArray("k");
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] is null) throw new InvalidOperationException("A cursor key value is null; null key values are not supported.");
                JsonSerializer.Serialize(writer, values[i], KeyTypeInfo(types[i]));
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Base64Url.EncodeToString(stream.ToArray());
    }

    /// <summary>Reads a cursor, or throws <see cref="InvalidCursorException"/>.</summary>
    /// <param name="cursor">The cursor text from the client.</param>
    /// <param name="types">The declared CLR type of each key, in order.</param>
    /// <exception cref="InvalidCursorException">The cursor does not fit the rules above.</exception>
    internal static CursorValues Decode(string cursor, IReadOnlyList<Type> types)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(types);
        if (cursor.Length == 0) throw new InvalidCursorException("The cursor is empty.");
        if (cursor.Length > MaxLength) throw new InvalidCursorException($"The cursor is longer than {MaxLength} characters.");

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(cursor);
        }
        catch (FormatException exception)
        {
            throw new InvalidCursorException("The cursor is not valid base64url.", exception);
        }

        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            return Read(document.RootElement, types);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or NotSupportedException or FormatException or OverflowException)
        {
            throw new InvalidCursorException("The cursor does not hold the expected position.", exception);
        }
    }

    /// <summary>Metadata for a cursor key type: source-generated for the common key types (no reflection), the default resolver for any other type.</summary>
    private static JsonTypeInfo KeyTypeInfo(Type type) => CursorJsonContext.Default.GetTypeInfo(type) ?? ReflectionTypeInfo(type);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Only key types missing from CursorJsonContext reach this path. With reflection enabled they serialise as before; when it is disabled (Native AOT) GetTypeInfo throws NotSupportedException, which the cursor reader reports as an invalid cursor and the writer surfaces to the caller.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "See IL2026.")]
    private static JsonTypeInfo ReflectionTypeInfo(Type type) => JsonSerializerOptions.Default.GetTypeInfo(type);

    private static CursorDirection? ReadDirection(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) return null;
        var text = element.GetString();
        if (text == "n") return CursorDirection.Next;
        return text == "p" ? CursorDirection.Previous : null;
    }

    private static CursorValues Read(JsonElement root, IReadOnlyList<Type> types)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidCursorException("The cursor does not hold the expected position.");

        CursorDirection? direction = null;
        JsonElement? keys = null;
        var count = 0;
        foreach (var property in root.EnumerateObject())
        {
            count++;
            if (property.NameEquals("d") && direction is null)
            {
                direction = ReadDirection(property.Value);
            }
            else if (property.NameEquals("k") && keys is null)
            {
                keys = property.Value;
            }
            else
            {
                throw new InvalidCursorException("The cursor does not hold the expected position.");
            }
        }

        if (count != 2 || direction is null || keys is not { ValueKind: JsonValueKind.Array } array || array.GetArrayLength() != types.Count)
        {
            throw new InvalidCursorException("The cursor does not hold the expected position.");
        }

        var values = new object[types.Count];
        var index = 0;
        foreach (var element in array.EnumerateArray())
        {
            var value = element.ValueKind == JsonValueKind.Null ? null : JsonSerializer.Deserialize(element, KeyTypeInfo(types[index]));
            values[index++] = value ?? throw new InvalidCursorException("The cursor holds a null key value.");
        }

        return new CursorValues(direction.Value, values);
    }
}

/// <summary>Source-generated metadata for the cursor key types most tables use.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(TimeSpan))]
internal sealed partial class CursorJsonContext : JsonSerializerContext;
