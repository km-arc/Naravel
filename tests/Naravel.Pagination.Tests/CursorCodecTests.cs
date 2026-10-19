using System.Buffers.Text;
using System.Text;

namespace Naravel.Pagination.Tests;

public class CursorCodecTests
{
    private static readonly Type[] IntAndText = [typeof(int), typeof(string)];

    [Fact]
    public void A_cursor_round_trips_its_direction_and_values()
    {
        var cursor = CursorCodec.Encode(CursorDirection.Previous, new object?[] { 42, "ab" }, IntAndText);

        var decoded = CursorCodec.Decode(cursor, IntAndText);

        decoded.Direction.Should().Be(CursorDirection.Previous);
        decoded.Values.Should().Equal(42, "ab");
    }

    [Fact]
    public void A_cursor_is_base64url_json_with_d_and_k()
    {
        var cursor = CursorCodec.Encode(CursorDirection.Next, new object?[] { 7 }, new[] { typeof(int) });

        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Should().Be("""{"d":"n","k":[7]}""");
        cursor.Should().NotContainAny("+", "/", "=");
    }

    [Fact]
    public void Dates_and_decimals_round_trip()
    {
        var types = new[] { typeof(DateTime), typeof(decimal) };
        var date = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

        var decoded = CursorCodec.Decode(CursorCodec.Encode(CursorDirection.Next, new object?[] { date, 12.50m }, types), types);

        decoded.Values.Should().Equal(date, 12.50m);
    }

    [Fact]
    public void A_null_value_cannot_be_encoded()
    {
        var call = () => CursorCodec.Encode(CursorDirection.Next, new object?[] { null }, new[] { typeof(int) });

        call.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Encoding_needs_one_type_per_value()
    {
        var call = () => CursorCodec.Encode(CursorDirection.Next, new object?[] { 1, 2 }, new[] { typeof(int) });

        call.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("***")]
    [InlineData("e30")]
    [InlineData("W10")] // []
    [InlineData("eyJkIjoibiIsImsiOjF9")] // k is not an array
    [InlineData("eyJkIjoibiIsImsiOlsxXSwieCI6MX0")] // extra property
    [InlineData("eyJkIjoibiIsImQiOiJuIn0")] // duplicate d, no k
    [InlineData("eyJkIjoibiIsImsiOlsxXX0")] // one value for two keys
    public void A_malformed_cursor_is_rejected(string cursor)
    {
        var call = () => CursorCodec.Decode(cursor, IntAndText);

        call.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void A_cursor_over_the_limit_is_rejected_and_not_echoed()
    {
        var call = () => CursorCodec.Decode(new string('Z', CursorCodec.MaxLength + 1), IntAndText);

        call.Should().Throw<InvalidCursorException>().Which.Message.Should().NotContain("ZZZ");
    }

    [Fact]
    public void A_cursor_at_the_limit_is_read_not_rejected_for_length()
    {
        var call = () => CursorCodec.Decode(new string('A', CursorCodec.MaxLength), IntAndText);

        call.Should().Throw<InvalidCursorException>().Which.Message.Should().NotContain("longer");
    }
}
