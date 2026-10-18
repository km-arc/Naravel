using Naravel.Database;

namespace Naravel.Database.Tests;

public sealed class ValueConverterTests
{
    private enum Color
    {
        Red = 3,
        Blue = 7
    }

    [Fact]
    public void Null_and_DBNull_become_the_default()
    {
        ValueConverter.Convert<int>(null).Should().Be(0);
        ValueConverter.Convert<string>(DBNull.Value).Should().BeNull();
        ValueConverter.Convert<int?>(DBNull.Value).Should().BeNull();
        ValueConverter.Convert<Guid>(null).Should().Be(Guid.Empty);
    }

    [Fact]
    public void A_value_that_already_has_the_type_is_returned_as_it_is()
    {
        ValueConverter.Convert<long>(5L).Should().Be(5L);
        ValueConverter.Convert<object>(7).Should().Be(7);
        ValueConverter.Convert<byte[]>(new byte[] { 1, 2 }).Should().Equal(1, 2);
    }

    [Theory]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData((sbyte)1, true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    public void Booleans_come_from_numbers_and_text(object raw, bool expected) => ValueConverter.Convert<bool>(raw).Should().Be(expected);

    [Fact]
    public void Numbers_convert_between_widths_and_from_double_to_decimal()
    {
        ValueConverter.Convert<int>(5L).Should().Be(5);
        ValueConverter.Convert<long>(5).Should().Be(5L);
        ValueConverter.Convert<decimal>(12345.6789d).Should().Be(12345.6789m);
        ValueConverter.Convert<long?>(9).Should().Be(9L);
    }

    [Fact]
    public void Guid_comes_from_text_and_from_sixteen_bytes()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        ValueConverter.Convert<Guid>(id.ToString().ToUpperInvariant()).Should().Be(id);
        ValueConverter.Convert<Guid>(id.ToByteArray()).Should().Be(id);
        var act = () => ValueConverter.Convert<Guid>(5);
        act.Should().Throw<InvalidCastException>();
    }

    [Fact]
    public void Dates_and_times_come_from_text_and_from_wider_provider_types()
    {
        ValueConverter.Convert<DateTime>("2026-10-18 12:30:45").Should().Be(new DateTime(2026, 10, 18, 12, 30, 45));
        ValueConverter.Convert<DateOnly>("2026-10-18").Should().Be(new DateOnly(2026, 10, 18));
        ValueConverter.Convert<DateOnly>(new DateTime(2026, 10, 18, 5, 0, 0)).Should().Be(new DateOnly(2026, 10, 18));
        ValueConverter.Convert<TimeOnly>("12:30:45").Should().Be(new TimeOnly(12, 30, 45));
        ValueConverter.Convert<TimeOnly>(new TimeSpan(12, 30, 45)).Should().Be(new TimeOnly(12, 30, 45));
        ValueConverter.Convert<DateTimeOffset>("2026-10-18 12:30:45+00:00").Should().Be(new DateTimeOffset(2026, 10, 18, 12, 30, 45, TimeSpan.Zero));
    }

    [Fact]
    public void An_unspecified_DateTime_is_read_as_UTC_when_a_DateTimeOffset_is_asked_for()
    {
        var read = ValueConverter.Convert<DateTimeOffset>(new DateTime(2026, 10, 18, 12, 30, 45, DateTimeKind.Unspecified));

        read.Offset.Should().Be(TimeSpan.Zero);
        read.UtcDateTime.Should().Be(new DateTime(2026, 10, 18, 12, 30, 45));
    }

    [Fact]
    public void Enums_come_from_the_underlying_number_or_the_name()
    {
        ValueConverter.Convert<Color>(7L).Should().Be(Color.Blue);
        ValueConverter.Convert<Color>("red").Should().Be(Color.Red);
    }

    [Fact]
    public void Strings_come_from_any_value()
    {
        ValueConverter.Convert<string>(12).Should().Be("12");
        ValueConverter.Convert<string>(1.5d).Should().Be("1.5");
    }

    [Fact]
    public void IsSimple_separates_single_values_from_classes_that_Dapper_maps()
    {
        ValueConverter.IsSimple(typeof(int?)).Should().BeTrue();
        ValueConverter.IsSimple(typeof(string)).Should().BeTrue();
        ValueConverter.IsSimple(typeof(Guid)).Should().BeTrue();
        ValueConverter.IsSimple(typeof(Color)).Should().BeTrue();
        ValueConverter.IsSimple(typeof(byte[])).Should().BeTrue();
        ValueConverter.IsSimple(typeof(object)).Should().BeTrue();
        ValueConverter.IsSimple(typeof(ContractPerson)).Should().BeFalse();
    }
}
