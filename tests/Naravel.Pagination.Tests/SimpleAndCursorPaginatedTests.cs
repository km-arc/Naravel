using System.Text.Json;
using Naravel.Pagination;

namespace Naravel.Pagination.Tests;

/// <summary>The simple and cursor shapes, plus the dependency boundary of the core package (PDR-020).</summary>
public class SimpleAndCursorPaginatedTests
{
    [Fact]
    public void Simple_page_has_no_total_or_last_page()
    {
        var page = new SimplePaginated<int>([1, 2], currentPage: 1, perPage: 2, hasMorePages: true).WithPath("/p");

        JsonSerializer.Serialize(page).Should().Be(
            """{"data":[1,2],"links":{"first":"/p?page=1","prev":null,"next":"/p?page=2"},"meta":{"current_page":1,"from":1,"path":"/p","per_page":2,"to":2}}""");
    }

    [Fact]
    public void Simple_last_page_has_a_previous_link_and_no_next()
    {
        var page = new SimplePaginated<int>([5], currentPage: 3, perPage: 2, hasMorePages: false).WithPath("/p");

        page.Links.Previous.Should().Be("/p?page=2");
        page.Links.Next.Should().BeNull();
        page.From.Should().Be(5);
        page.To.Should().Be(5);
    }

    [Fact]
    public void Simple_empty_page_has_null_from_and_to_and_map_keeps_the_flags()
    {
        var page = new SimplePaginated<int>([], currentPage: 4, perPage: 2, hasMorePages: false);

        page.From.Should().BeNull();
        page.To.Should().BeNull();
        page.Map(i => i.ToString()).HasMorePages.Should().BeFalse();
    }

    [Fact]
    public void Simple_page_rejects_more_items_than_the_page_size()
    {
        ((Action)(() => new SimplePaginated<int>([1, 2, 3], 1, 2, true))).Should().Throw<ArgumentException>();
        ((Action)(() => new SimplePaginated<int>([], 0, 2, false))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Cursor_page_serializes_with_cursors_in_links_and_meta()
    {
        var page = new CursorPaginated<int>([1, 2], perPage: 2, nextCursor: "abc", previousCursor: null).WithPath("/p");

        JsonSerializer.Serialize(page).Should().Be(
            """{"data":[1,2],"links":{"prev":null,"next":"/p?cursor=abc"},"meta":{"path":"/p","per_page":2,"next_cursor":"abc","prev_cursor":null}}""");
        page.HasMorePages.Should().BeTrue();
        page.OnFirstPage.Should().BeTrue();
    }

    [Fact]
    public void Cursor_values_and_query_values_are_encoded_and_the_cursor_name_can_change()
    {
        var page = new CursorPaginated<int>([1], perPage: 5, nextCursor: "a+b/c=", previousCursor: "p")
            .WithPath("/p").WithQuery([new("q", "x y"), new("cursor", "old")]).WithCursorName("c");

        page.Links.Next.Should().Be("/p?q=x%20y&cursor=old&c=a%2Bb%2Fc%3D");
        page.Links.Previous.Should().Be("/p?q=x%20y&cursor=old&c=p");
    }

    [Fact]
    public void Cursor_page_without_a_path_has_null_links_but_keeps_the_cursors()
    {
        var page = new CursorPaginated<int>([1], 5, "n", "p");

        page.Links.Next.Should().BeNull();
        page.Links.Previous.Should().BeNull();
        page.Meta.NextCursor.Should().Be("n");
        page.Meta.PreviousCursor.Should().Be("p");
        page.Map(i => i * 2).Items.Should().Equal(2);
    }

    [Fact]
    public void Cursor_page_rejects_more_items_than_the_page_size()
    {
        ((Action)(() => new CursorPaginated<int>([1, 2, 3], 2, null, null))).Should().Throw<ArgumentException>();
        ((Action)(() => new CursorPaginated<int>([], 0, null, null))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_core_package_references_no_framework_beyond_the_base_library()
    {
        var references = typeof(Paginated<int>).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        references.Should().NotContain(name => name.StartsWith("Microsoft.", StringComparison.Ordinal) || name.StartsWith("Naravel.", StringComparison.Ordinal));
    }
}
