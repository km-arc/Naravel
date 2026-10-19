using Microsoft.EntityFrameworkCore;

namespace Naravel.Pagination.EntityFrameworkCore.Tests;

public class CursorPagingTests
{
    private static CursorKeys<Post> ById(CursorKeys<Post> k) => k.By(p => p.Id);

    private static async Task<List<int>> WalkForward(IQueryable<Post> query, Func<CursorKeys<Post>, CursorKeys<Post>> keys, int perPage)
    {
        var ids = new List<int>();
        string? cursor = null;
        for (var guard = 0; guard < 100; guard++)
        {
            var page = await query.CursorPaginateAsync(keys, perPage, cursor);
            ids.AddRange(page.Items.Select(p => p.Id));
            if (page.NextCursor is null) return ids;
            cursor = page.NextCursor;
        }

        throw new InvalidOperationException("The walk did not end.");
    }

    [Fact]
    public async Task The_first_page_has_a_next_cursor_and_no_previous_cursor()
    {
        using var db = new PostDb(25);

        var page = await db.Context.Posts.CursorPaginateAsync(ById, 10, null);

        page.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(1, 10));
        page.NextCursor.Should().NotBeNull();
        page.PreviousCursor.Should().BeNull();
    }

    [Fact]
    public async Task Walking_forward_visits_every_row_once_in_order()
    {
        using var db = new PostDb(25);

        var ids = await WalkForward(db.Context.Posts, ById, 10);

        ids.Should().Equal(Enumerable.Range(1, 25));
    }

    [Fact]
    public async Task The_last_page_has_no_next_cursor_and_an_exact_fit_does_not_invent_one()
    {
        using var db = new PostDb(20);

        var second = await db.Context.Posts.CursorPaginateAsync(ById, 10, (await db.Context.Posts.CursorPaginateAsync(ById, 10, null)).NextCursor);

        second.Items.Should().HaveCount(10);
        second.NextCursor.Should().BeNull();
        second.PreviousCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task Walking_back_with_the_previous_cursor_returns_the_earlier_page_in_order()
    {
        using var db = new PostDb(25);
        var first = await db.Context.Posts.CursorPaginateAsync(ById, 10, null);
        var second = await db.Context.Posts.CursorPaginateAsync(ById, 10, first.NextCursor);
        var third = await db.Context.Posts.CursorPaginateAsync(ById, 10, second.NextCursor);

        var back = await db.Context.Posts.CursorPaginateAsync(ById, 10, third.PreviousCursor);
        var backAgain = await db.Context.Posts.CursorPaginateAsync(ById, 10, back.PreviousCursor);

        back.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(11, 10));
        back.NextCursor.Should().NotBeNull();
        back.PreviousCursor.Should().NotBeNull();
        backAgain.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(1, 10));
        backAgain.PreviousCursor.Should().BeNull();
    }

    [Fact]
    public async Task A_row_inserted_behind_the_cursor_does_not_shift_the_next_page()
    {
        using var db = new PostDb(25);
        var first = await db.Context.Posts.CursorPaginateAsync(ById, 10, null);

        db.Context.Posts.Add(new Post { Id = -5, Title = "early", CreatedAt = new DateTime(2020, 1, 1) });
        await db.Context.SaveChangesAsync();
        var second = await db.Context.Posts.CursorPaginateAsync(ById, 10, first.NextCursor);

        second.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(11, 10));
    }

    [Fact]
    public async Task Offset_paging_does_shift_under_the_same_insert()
    {
        using var db = new PostDb(25);
        var first = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 10);

        db.Context.Posts.Add(new Post { Id = -5, Title = "early", CreatedAt = new DateTime(2020, 1, 1) });
        await db.Context.SaveChangesAsync();
        var second = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(2, 10);

        first.Items.Last().Id.Should().Be(10);
        second.Items.First().Id.Should().Be(10, "the inserted row pushed row 10 onto page two; this is the cost keyset paging avoids");
    }

    [Fact]
    public async Task A_descending_key_walks_from_high_to_low_and_back()
    {
        using var db = new PostDb(25);
        Func<CursorKeys<Post>, CursorKeys<Post>> keys = k => k.By(p => p.Id, descending: true);

        var first = await db.Context.Posts.CursorPaginateAsync(keys, 10, null);
        var second = await db.Context.Posts.CursorPaginateAsync(keys, 10, first.NextCursor);
        var back = await db.Context.Posts.CursorPaginateAsync(keys, 10, second.PreviousCursor);

        first.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(16, 10).Reverse());
        second.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(6, 10).Reverse());
        back.Items.Select(p => p.Id).Should().Equal(first.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task Several_keys_break_ties_with_mixed_directions()
    {
        using var db = new PostDb(30);
        Func<CursorKeys<Post>, CursorKeys<Post>> keys = k => k.By(p => p.Bucket).Then(p => p.Id, descending: true);
        var expected = db.Context.Posts.AsEnumerable().OrderBy(p => p.Bucket).ThenByDescending(p => p.Id).Select(p => p.Id).ToList();

        var ids = await WalkForward(db.Context.Posts, keys, 4);

        ids.Should().Equal(expected);
    }

    [Fact]
    public async Task Walking_back_through_several_keys_mirrors_walking_forward()
    {
        using var db = new PostDb(30);
        Func<CursorKeys<Post>, CursorKeys<Post>> keys = k => k.By(p => p.Bucket).Then(p => p.Id);
        var pages = new List<List<int>>();
        string? cursor = null;
        CursorPaginated<Post> page;
        do
        {
            page = await db.Context.Posts.CursorPaginateAsync(keys, 7, cursor);
            pages.Add(page.Items.Select(p => p.Id).ToList());
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var backwards = new List<List<int>> { page.Items.Select(p => p.Id).ToList() };
        while (page.PreviousCursor is not null)
        {
            page = await db.Context.Posts.CursorPaginateAsync(keys, 7, page.PreviousCursor);
            backwards.Add(page.Items.Select(p => p.Id).ToList());
        }

        backwards.Reverse();
        backwards.Should().Equal(pages, (a, b) => a.SequenceEqual(b));
    }

    [Fact]
    public async Task A_string_key_is_compared_as_text()
    {
        using var db = new PostDb(25);

        var ids = await WalkForward(db.Context.Posts, k => k.By(p => p.Title), 6);

        ids.Should().Equal(Enumerable.Range(1, 25));
    }

    [Fact]
    public async Task A_date_key_with_the_id_as_tie_breaker_works()
    {
        using var db = new PostDb(25);
        db.Context.Posts.Add(new Post { Id = 100, Title = "same-day", CreatedAt = new DateTime(2026, 1, 1).AddDays(3) });
        await db.Context.SaveChangesAsync();
        var expected = db.Context.Posts.AsEnumerable().OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).Select(p => p.Id).ToList();

        var ids = await WalkForward(db.Context.Posts, k => k.By(p => p.CreatedAt).Then(p => p.Id), 5);

        ids.Should().Equal(expected);
    }

    [Fact]
    public async Task A_filter_and_an_old_order_by_are_handled()
    {
        using var db = new PostDb(30);

        var page = await db.Context.Posts.Where(p => p.Bucket == 1).OrderByDescending(p => p.Title).CursorPaginateAsync(ById, 3, null);

        page.Items.Select(p => p.Id).Should().Equal(1, 4, 7);
    }

    [Fact]
    public async Task A_cursor_past_the_last_row_gives_an_empty_page()
    {
        using var db = new PostDb(5);
        var last = await db.Context.Posts.CursorPaginateAsync(ById, 5, null);
        var cursor = CursorCodec.Encode(CursorDirection.Next, new object?[] { 5 }, new[] { typeof(int) });

        var page = await db.Context.Posts.CursorPaginateAsync(ById, 5, cursor);

        last.NextCursor.Should().BeNull();
        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
        page.PreviousCursor.Should().BeNull();
    }

    [Fact]
    public async Task The_page_size_of_a_cursor_page_is_clamped()
    {
        using var db = new PostDb(150);

        var page = await db.Context.Posts.CursorPaginateAsync(ById, 1000, null);

        page.PerPage.Should().Be(100);
        page.Items.Should().HaveCount(100);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a cursor!")]
    [InlineData("e30")] // {}
    [InlineData("eyJkIjoibiIsImsiOltdfQ")] // {"d":"n","k":[]}
    [InlineData("eyJkIjoieCIsImsiOlsxXX0")] // {"d":"x","k":[1]}
    [InlineData("eyJkIjoibiIsImsiOlsiYWJjIl19")] // {"d":"n","k":["abc"]}: wrong type for an int key
    [InlineData("eyJkIjoibiIsImsiOltudWxsXX0")] // {"d":"n","k":[null]}
    [InlineData("eyJkIjoibiIsImsiOlsxLDJdfQ")] // {"d":"n","k":[1,2]}: too many values
    public void A_cursor_that_cannot_be_read_is_rejected_at_the_call(string cursor)
    {
        using var db = new PostDb(5);

        Action call = () => db.Context.Posts.CursorPaginateAsync(ById, 5, cursor);

        call.Should().Throw<InvalidCursorException>();
    }

    [Fact]
    public void A_cursor_over_the_length_limit_is_rejected_without_being_echoed()
    {
        using var db = new PostDb(5);
        var cursor = new string('A', 1025);

        Action call = () => db.Context.Posts.CursorPaginateAsync(ById, 5, cursor);

        call.Should().Throw<InvalidCursorException>().Which.Message.Should().NotContain("AAAA");
    }

    [Fact]
    public async Task A_null_key_value_on_the_page_edge_is_a_clear_error()
    {
        using var db = new PostDb(5);
        db.Context.Posts.Add(new Post { Id = 99, Title = "no-score", Score = null });
        await db.Context.SaveChangesAsync();

        var call = () => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Score).Then(p => p.Id), 2, null);

        (await FluentActions.Awaiting(call).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Score").And.Contain("null");
    }

    [Fact]
    public async Task A_nullable_key_works_when_no_value_is_null()
    {
        using var db = new PostDb(12);

        var ids = await WalkForward(db.Context.Posts, k => k.By(p => p.Score).Then(p => p.Id), 5);

        ids.Should().Equal(Enumerable.Range(1, 12));
    }

    [Fact]
    public void A_key_type_without_a_comparison_fails_when_it_is_declared()
    {
        using var db = new PostDb(1);

        Action call = () => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Token), 5, null);

        call.Should().Throw<NotSupportedException>().WithMessage("*Token*");
    }

    [Fact]
    public void A_key_that_is_not_a_member_access_is_rejected()
    {
        using var db = new PostDb(1);

        Action call = () => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Id + 1), 5, null);

        call.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Declaring_no_key_is_rejected()
    {
        using var db = new PostDb(1);

        Action call = () => db.Context.Posts.CursorPaginateAsync(k => k, 5, null);

        call.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Then_before_By_and_By_twice_are_rejected()
    {
        using var db = new PostDb(1);

        ((Action)(() => db.Context.Posts.CursorPaginateAsync(k => k.Then(p => p.Id), 5, null))).Should().Throw<InvalidOperationException>();
        ((Action)(() => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Id).By(p => p.Bucket), 5, null))).Should().Throw<InvalidOperationException>();
    }
}
