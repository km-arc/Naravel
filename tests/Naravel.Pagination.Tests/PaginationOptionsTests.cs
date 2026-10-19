namespace Naravel.Pagination.Tests;

public class PaginationOptionsTests
{
    [Fact]
    public void The_default_maximum_is_100()
    {
        PaginationOptions.Default.MaxPerPage.Should().Be(100);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public void A_page_size_is_clamped_to_the_maximum(int requested, int expected)
    {
        PaginationOptions.Default.ClampPerPage(requested).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_page_size_below_one_throws(int requested)
    {
        var call = () => PaginationOptions.Default.ClampPerPage(requested);

        call.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_custom_maximum_applies_and_must_be_positive()
    {
        new PaginationOptions { MaxPerPage = 5 }.ClampPerPage(50).Should().Be(5);

        var call = () => new PaginationOptions { MaxPerPage = 0 };

        call.Should().Throw<ArgumentOutOfRangeException>();
    }
}
