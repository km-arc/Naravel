using System.Text.Json.Serialization;

namespace Naravel.Database;

/// <summary>Source-generated metadata for the scalar values the JSON where and update methods send as JSON text, so they need no reflection (trimming and Native AOT).</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
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
internal sealed partial class QueryJsonContext : JsonSerializerContext;
