using Microsoft.EntityFrameworkCore;

namespace Naravel.Pagination.EntityFrameworkCore.Tests;

public class OffsetPagingTests
{
    [Fact]
    public async Task A_middle_page_has_its_rows_and_the_totals()
    {
        using var db = new PostDb(25);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(2, 10);

        page.Items.Select(p => p.Id).Should().Equal(11, 12, 13, 14, 15, 16, 17, 18, 19, 20);
        page.Total.Should().Be(25);
        page.CurrentPage.Should().Be(2);
        page.LastPage.Should().Be(3);
        page.From.Should().Be(11);
        page.To.Should().Be(20);
    }

    [Fact]
    public async Task The_last_page_is_partial()
    {
        using var db = new PostDb(25);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(3, 10);

        page.Items.Should().HaveCount(5);
        page.To.Should().Be(25);
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_with_the_right_totals()
    {
        using var db = new PostDb(25);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(9, 10);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(25);
        page.LastPage.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public async Task A_page_below_one_is_page_one(int requested)
    {
        using var db = new PostDb(25);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(requested, 10);

        page.CurrentPage.Should().Be(1);
        page.Items.First().Id.Should().Be(1);
    }

    [Fact]
    public async Task An_empty_table_is_one_empty_page()
    {
        using var db = new PostDb();

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 10);

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(0);
        page.LastPage.Should().Be(1);
        page.From.Should().BeNull();
        page.To.Should().BeNull();
    }

    [Fact]
    public async Task The_page_size_is_clamped_to_the_maximum()
    {
        using var db = new PostDb(120);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 500);

        page.PerPage.Should().Be(100);
        page.Items.Should().HaveCount(100);
    }

    [Fact]
    public async Task A_custom_maximum_is_used()
    {
        using var db = new PostDb(20);

        var page = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 50, new PaginationOptions { MaxPerPage = 7 });

        page.PerPage.Should().Be(7);
        page.Items.Should().HaveCount(7);
    }

    [Fact]
    public void A_page_size_below_one_throws_before_any_query_runs()
    {
        using var db = new PostDb(5);

        var call = () => db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 0);

        call.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_query_without_order_by_is_rejected()
    {
        using var db = new PostDb(5);

        var call = () => db.Context.Posts.PaginateAsync(1, 10);

        call.Should().Throw<InvalidOperationException>().WithMessage("*OrderBy*");
    }

    [Fact]
    public void An_order_by_after_a_filter_counts_as_ordered()
    {
        using var db = new PostDb(5);

        var call = () => db.Context.Posts.OrderByDescending(p => p.Id).Where(p => p.Id > 1).PaginateAsync(1, 10);

        call.Should().NotThrow();
    }

    [Fact]
    public async Task A_filter_is_kept_in_the_page_and_in_the_count()
    {
        using var db = new PostDb(30);

        var page = await db.Context.Posts.Where(p => p.Bucket == 0).OrderBy(p => p.Id).PaginateAsync(1, 4);

        page.Total.Should().Be(10);
        page.Items.Select(p => p.Id).Should().Equal(3, 6, 9, 12);
    }

    [Fact]
    public async Task Simple_paging_knows_whether_a_next_page_exists_without_counting()
    {
        using var db = new PostDb(25);
        var query = db.Context.Posts.OrderBy(p => p.Id);

        var first = await query.SimplePaginateAsync(1, 10);
        var last = await query.SimplePaginateAsync(3, 10);
        var exact = await db.Context.Posts.Where(p => p.Id <= 20).OrderBy(p => p.Id).SimplePaginateAsync(2, 10);

        first.HasMorePages.Should().BeTrue();
        first.Items.Should().HaveCount(10);
        last.HasMorePages.Should().BeFalse();
        last.Items.Should().HaveCount(5);
        exact.HasMorePages.Should().BeFalse();
        exact.Items.Should().HaveCount(10);
    }

    [Fact]
    public async Task Simple_paging_past_the_end_is_empty()
    {
        using var db = new PostDb(5);

        var page = await db.Context.Posts.OrderBy(p => p.Id).SimplePaginateAsync(4, 10);

        page.Items.Should().BeEmpty();
        page.HasMorePages.Should().BeFalse();
    }
}
