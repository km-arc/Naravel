using System.Text.Json;
using Naravel.Pagination;

namespace Naravel.Pagination.Tests;

/// <summary>Numbers, links and the exact JSON of a length-aware page (PDR-020).</summary>
public class PaginatedTests
{
    private static string Json<T>(T value, JsonSerializerOptions? options = null) => JsonSerializer.Serialize(value, options);

    [Fact]
    public void Middle_page_serializes_to_laravels_data_links_meta_shape()
    {
        var page = new Paginated<int>([16, 17, 18], total: 100, currentPage: 2, perPage: 15).WithPath("/posts");

        Json(page).Should().Be(
            """{"data":[16,17,18],"links":{"first":"/posts?page=1","last":"/posts?page=7","prev":"/posts?page=1","next":"/posts?page=3"},"meta":{"current_page":2,"from":16,"last_page":7,"path":"/posts","per_page":15,"to":18,"total":100}}""");
    }

    [Fact]
    public void Without_a_path_every_link_and_the_path_are_null_not_missing()
    {
        var page = new Paginated<int>([1, 2], total: 2, currentPage: 1, perPage: 15);

        Json(page).Should().Be(
            """{"data":[1,2],"links":{"first":null,"last":null,"prev":null,"next":null},"meta":{"current_page":1,"from":1,"last_page":1,"path":null,"per_page":15,"to":2,"total":2}}""");
    }

    [Fact]
    public void An_empty_set_has_one_empty_page_with_null_from_and_to()
    {
        var page = new Paginated<int>([], total: 0, currentPage: 1, perPage: 15).WithPath("/p");

        Json(page).Should().Be(
            """{"data":[],"links":{"first":"/p?page=1","last":"/p?page=1","prev":null,"next":null},"meta":{"current_page":1,"from":null,"last_page":1,"path":"/p","per_page":15,"to":null,"total":0}}""");
        page.LastPage.Should().Be(1);
        page.HasMorePages.Should().BeFalse();
    }

    [Fact]
    public void A_page_past_the_end_is_empty_with_correct_totals_and_a_previous_link()
    {
        var page = new Paginated<int>([], total: 100, currentPage: 9, perPage: 15).WithPath("/p");

        page.LastPage.Should().Be(7);
        page.From.Should().BeNull();
        page.To.Should().BeNull();
        page.HasMorePages.Should().BeFalse();
        page.OnLastPage.Should().BeTrue();
        page.Links.Previous.Should().Be("/p?page=8");
        page.Links.Next.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 15, 1)]
    [InlineData(1, 15, 1)]
    [InlineData(15, 15, 1)]
    [InlineData(16, 15, 2)]
    [InlineData(100, 15, 7)]
    [InlineData(105, 15, 7)]
    [InlineData(106, 15, 8)]
    [InlineData(9_000_000_000, 1, int.MaxValue)]
    public void Last_page_rounds_up_and_never_drops_below_one(long total, int perPage, int expected)
    {
        new Paginated<int>([], total, 1, perPage).LastPage.Should().Be(expected);
    }

    [Fact]
    public void First_and_last_page_flags_and_positions_follow_the_numbers()
    {
        var first = new Paginated<int>([1, 2, 3], total: 7, currentPage: 1, perPage: 3);
        var last = new Paginated<int>([7], total: 7, currentPage: 3, perPage: 3);

        first.OnFirstPage.Should().BeTrue();
        first.HasMorePages.Should().BeTrue();
        first.From.Should().Be(1);
        first.To.Should().Be(3);
        last.OnLastPage.Should().BeTrue();
        last.OnFirstPage.Should().BeFalse();
        last.From.Should().Be(7);
        last.To.Should().Be(7);
    }

    [Fact]
    public void Query_values_are_encoded_and_the_page_parameter_is_replaced_and_written_last()
    {
        var page = new Paginated<int>([1], total: 30, currentPage: 2, perPage: 10)
            .WithPath("/p")
            .WithQuery([new("sort", "votes"), new("page", "9"), new("q", "a b&c"), new("empty", null)]);

        page.Links.Next.Should().Be("/p?sort=votes&q=a%20b%26c&empty=&page=3");
        page.Links.First.Should().Be("/p?sort=votes&q=a%20b%26c&empty=&page=1");
    }

    [Fact]
    public void A_path_that_already_has_a_query_string_gets_an_ampersand()
    {
        new Paginated<int>([1], 30, 1, 10).WithPath("/p?x=1").Links.Next.Should().Be("/p?x=1&page=2");
    }

    [Fact]
    public void The_page_parameter_can_be_renamed()
    {
        var page = new Paginated<int>([1], 30, 1, 10).WithPath("/p").WithPageName("p");

        page.Links.Next.Should().Be("/p?p=2");
        ((Action)(() => page.WithPageName(" "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Map_keeps_numbers_path_query_and_page_name_and_never_changes_the_original()
    {
        var page = new Paginated<int>([1, 2], total: 30, currentPage: 2, perPage: 2)
            .WithPath("/p").WithQuery([new("a", "1")]).WithPageName("pg");

        var mapped = page.Map(i => $"#{i}");

        mapped.Items.Should().Equal("#1", "#2");
        mapped.Total.Should().Be(30);
        mapped.Links.Next.Should().Be("/p?a=1&pg=3");
        page.Items.Should().Equal(1, 2);
    }

    [Fact]
    public void With_methods_return_new_instances()
    {
        var page = new Paginated<int>([1], 30, 1, 10);

        page.WithPath("/p").Should().NotBeSameAs(page);
        page.Links.First.Should().BeNull();
    }

    [Fact]
    public void Output_is_the_same_when_the_application_uses_camel_case_and_ignores_nulls()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var page = new Paginated<int>([], total: 0, currentPage: 1, perPage: 15);

        Json(page, options).Should().Be(Json(page));
        Json(page, options).Should().Contain("\"path\":null").And.Contain("\"from\":null");
    }

    [Fact]
    public void Constructor_rejects_impossible_numbers()
    {
        ((Action)(() => new Paginated<int>([], -1, 1, 15))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new Paginated<int>([], 0, 0, 15))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new Paginated<int>([], 0, 1, 0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => new Paginated<int>(null!, 0, 1, 15))).Should().Throw<ArgumentNullException>();
        ((Action)(() => new Paginated<int>([], 0, 1, 15).WithPath(""))).Should().Throw<ArgumentException>();
    }
}
