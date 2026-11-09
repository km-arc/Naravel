using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Naravel.Pagination.EntityFrameworkCore.Tests;

// Runs the quickstart, the Testing examples and the messages of docs/en/pagination.md.
// The guide's Post and AppDb are Post and PostContext of PostDb.cs without the columns the guide does not use, and the guide's seed
// (25 posts, "post-001" ..., one day apart from 2026-01-02) is the one of PostDb(25). If a test here needs a change, the guide needs the same change.
public sealed class DocsExampleTests
{
    // The model of the guide's Quickstart and Testing example, with the same shape and under the guide's names (nested here so that the
    // Post of PostDb.cs, which the other tests use, keeps its name).
    public static class GuideModel
    {
        public sealed class Post
        {
            public int Id { get; set; }

            public string Title { get; set; } = "";

            public DateTime CreatedAt { get; set; }
        }

        public sealed class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
        {
            public DbSet<Post> Posts => Set<Post>();
        }
    }

    private static readonly Func<CursorKeys<Post>, CursorKeys<Post>> Feed = k => k.By(p => p.CreatedAt, descending: true).Then(p => p.Id);

    private static string Cursor(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

    [Fact]
    public async Task The_length_aware_endpoint_of_the_quickstart_returns_the_documented_page()
    {
        using var db = new PostDb(25);
        var noExtraQuery = Array.Empty<KeyValuePair<string, string?>>();   // GET /posts?page=3 has nothing but the page parameter

        var page = (await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(3, 10)).WithPath("/posts").WithQuery(noExtraQuery);

        page.Items.Select(p => p.Id).Should().Equal(21, 22, 23, 24, 25);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            page.Map(p => new { p.Id, p.Title, p.CreatedAt }), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        json.RootElement.GetProperty("data")[0].GetRawText().Should().Be("""{"id":21,"title":"post-021","createdAt":"2026-01-22T00:00:00"}""");
        json.RootElement.GetProperty("links").GetRawText().Should().Be("""{"first":"/posts?page=1","last":"/posts?page=3","prev":"/posts?page=2","next":null}""");
        json.RootElement.GetProperty("meta").GetRawText().Should().Be(
            """{"current_page":3,"from":21,"last_page":3,"path":"/posts","per_page":10,"to":25,"total":25}""");
    }

    [Fact]
    public async Task The_simple_endpoint_knows_only_whether_a_next_page_exists()
    {
        using var db = new PostDb(25);

        var last = await db.Context.Posts.OrderBy(p => p.Id).SimplePaginateAsync(3, 10);
        var middle = await db.Context.Posts.OrderBy(p => p.Id).SimplePaginateAsync(2, 10);

        last.Items.Select(p => p.Id).Should().Equal(21, 22, 23, 24, 25);
        last.HasMorePages.Should().BeFalse();
        middle.Items.Should().HaveCount(10);
        middle.HasMorePages.Should().BeTrue();
    }

    [Fact]
    public async Task The_feed_endpoint_walks_forward_and_back_with_the_cursors_it_returns()
    {
        using var db = new PostDb(25);

        var first = await db.Context.Posts.CursorPaginateAsync(Feed, 10, null);
        var second = await db.Context.Posts.CursorPaginateAsync(Feed, 10, first.NextCursor);
        var third = await db.Context.Posts.CursorPaginateAsync(Feed, 10, second.NextCursor);
        var back = await db.Context.Posts.CursorPaginateAsync(Feed, 10, third.PreviousCursor);

        first.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(16, 10).Reverse());
        first.PreviousCursor.Should().BeNull();
        second.Items.Select(p => p.Id).Should().Equal(Enumerable.Range(6, 10).Reverse());
        third.Items.Select(p => p.Id).Should().Equal(5, 4, 3, 2, 1);
        third.NextCursor.Should().BeNull();
        back.Items.Select(p => p.Id).Should().Equal(second.Items.Select(p => p.Id));
        first.WithPath("/posts/feed").Links.Next.Should().Be($"/posts/feed?cursor={first.NextCursor}");
    }

    [Fact]
    public async Task Page_edges_are_the_ones_in_the_guide()
    {
        using var db = new PostDb(25);

        (await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(0, 10)).CurrentPage.Should().Be(1);
        var past = await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(9, 10);
        past.Items.Should().BeEmpty();
        past.Total.Should().Be(25);
        past.LastPage.Should().Be(3);
        (await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 500)).PerPage.Should().Be(100);
        (await db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 500, new PaginationOptions { MaxPerPage = 50 })).PerPage.Should().Be(50);

        using var empty = new PostDb();
        var none = await empty.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 10);
        none.LastPage.Should().Be(1);
        none.From.Should().BeNull();
        none.To.Should().BeNull();
    }

    [Fact]
    public async Task The_testing_example_pages_an_in_memory_sqlite_database_seeded_like_the_quickstart()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();   // the database lives as long as this connection
        var options = new DbContextOptionsBuilder<GuideModel.AppDb>().UseSqlite(connection).Options;
        using var db = new GuideModel.AppDb(options);
        db.Database.EnsureCreated().Should().BeTrue();
        for (var i = 1; i <= 25; i++)
        {
            db.Posts.Add(new GuideModel.Post { Title = $"post-{i:000}", CreatedAt = new DateTime(2026, 1, 1).AddDays(i) });
        }

        db.SaveChanges();

        var page = await db.Posts.OrderBy(p => p.Id).PaginateAsync(page: 3, perPage: 10);

        page.Items.Select(p => p.Title).Should().Equal("post-021", "post-022", "post-023", "post-024", "post-025");
        page.Total.Should().Be(25);
        page.LastPage.Should().Be(3);
        page.HasMorePages.Should().BeFalse();
    }

    [Fact]
    public void Pages_can_be_built_by_hand_for_a_test_that_has_no_database()
    {
        var page = new Paginated<int>([21, 22, 23, 24, 25], total: 25, currentPage: 3, perPage: 10).WithPath("/posts");
        var simple = new SimplePaginated<int>([1, 2], currentPage: 1, perPage: 2, hasMorePages: true).WithPath("/posts");
        var feed = new CursorPaginated<int>([1, 2], perPage: 2, nextCursor: "abc", previousCursor: null).WithPath("/posts/feed");

        page.LastPage.Should().Be(3);
        page.HasMorePages.Should().BeFalse();
        page.Links.Previous.Should().Be("/posts?page=2");
        simple.Links.Next.Should().Be("/posts?page=2");
        feed.Links.Next.Should().Be("/posts/feed?cursor=abc");
        feed.OnFirstPage.Should().BeTrue();
    }

    [Fact]
    public async Task Size_and_ordering_errors_carry_the_messages_in_the_guide()
    {
        using var db = new PostDb(5);

        var zero = await FluentActions.Invoking(() => db.Context.Posts.OrderBy(p => p.Id).PaginateAsync(1, 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        zero.Which.ParamName.Should().Be("perPage");
        var zeroSimple = await FluentActions.Invoking(() => db.Context.Posts.OrderBy(p => p.Id).SimplePaginateAsync(1, 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        zeroSimple.Which.ParamName.Should().Be("perPage");
        var zeroCursor = await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(Feed, 0, null)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        zeroCursor.Which.ParamName.Should().Be("perPage");
        FluentActions.Invoking(() => new PaginationOptions { MaxPerPage = 0 }).Should().Throw<ArgumentOutOfRangeException>().WithParameterName("value");
        FluentActions.Invoking(() => new PaginationOptions().ClampPerPage(0)).Should().Throw<ArgumentOutOfRangeException>().WithParameterName("perPage");

        const string unordered = "The query has no OrderBy. A page of an unordered query is not stable; order it, ending with a unique column.";
        await FluentActions.Invoking(() => db.Context.Posts.PaginateAsync(1, 10)).Should().ThrowAsync<InvalidOperationException>().WithMessage(unordered);
        await FluentActions.Invoking(() => db.Context.Posts.SimplePaginateAsync(1, 10)).Should().ThrowAsync<InvalidOperationException>().WithMessage(unordered);

        FluentActions.Invoking(() => new Paginated<int>([], total: 0, currentPage: 0, perPage: 10)).Should().Throw<ArgumentOutOfRangeException>().WithParameterName("currentPage");
        FluentActions.Invoking(() => new Paginated<int>([], total: 0, currentPage: 1, perPage: 0)).Should().Throw<ArgumentOutOfRangeException>().WithParameterName("perPage");
        FluentActions.Invoking(() => new Paginated<int>([], total: -1, currentPage: 1, perPage: 10)).Should().Throw<ArgumentOutOfRangeException>().WithParameterName("total");
        var page = new Paginated<int>([], total: 0, currentPage: 1, perPage: 10);
        FluentActions.Invoking(() => page.WithPath(" ")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => page.WithPageName("")).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new CursorPaginated<int>([], 2, null, null).WithCursorName("")).Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => new SimplePaginated<int>([1, 2, 3], 1, 2, false))
            .Should().Throw<ArgumentException>().WithMessage("A page cannot hold more items than the page size.*").WithParameterName("items");
        FluentActions.Invoking(() => new CursorPaginated<int>([1, 2, 3], 2, null, null))
            .Should().Throw<ArgumentException>().WithMessage("A page cannot hold more items than the page size.*").WithParameterName("items");
    }

    [Fact]
    public async Task Key_errors_carry_the_messages_in_the_guide()
    {
        using var db = new PostDb(5);

        await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k.Then(p => p.Id), 5, null))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("Declare the first key with By before Then.");
        await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Id).By(p => p.Bucket), 5, null))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("By declares the first key; use Then for the others.");
        var none = await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k, 5, null)).Should().ThrowAsync<ArgumentException>();
        none.Which.Message.Should().StartWith("Declare at least one key with By.");
        none.Which.ParamName.Should().Be("keys");
        var notMember = await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Id + 1), 5, null)).Should().ThrowAsync<ArgumentException>();
        notMember.Which.Message.Should().StartWith("A cursor key must be a property or field access such as p => p.Id.");
        notMember.Which.ParamName.Should().Be("selector");

        await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Token), 5, null))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("The cursor key 'Token' has type Guid, which has no ordering the databases agree on. " +
                         "Use a number, DateTime, DateTimeOffset, TimeSpan, DateOnly, TimeOnly or string key (or its underlying number).");
    }

    [Fact]
    public async Task A_null_key_value_on_the_page_edge_carries_the_message_in_the_guide()
    {
        using var db = new PostDb(5);
        db.Context.Posts.Add(new Post { Id = 99, Title = "no-score", Score = null });
        await db.Context.SaveChangesAsync();

        // SQLite sorts NULL first, so with one row per page the null row is the last row of the first page and its key goes into the cursor.
        await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(k => k.By(p => p.Score).Then(p => p.Id), 1, null))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("The cursor key 'Score' is null on an item at the edge of the page; null key values are not supported. Use a key that is never null.");
    }

    [Fact]
    public async Task Cursor_errors_carry_the_messages_in_the_guide()
    {
        using var db = new PostDb(5);

        async Task Rejects(string cursor, string message) =>
            await FluentActions.Invoking(() => db.Context.Posts.CursorPaginateAsync(Feed, 5, cursor)).Should().ThrowAsync<InvalidCursorException>().WithMessage(message);

        await Rejects("", "The cursor is empty.");
        await Rejects(new string('A', 1025), "The cursor is longer than 1024 characters.");
        await Rejects("!!!", "The cursor is not valid base64url.");
        await Rejects(Cursor("""{"d":"n","k":[1]}"""), "The cursor does not hold the expected position.");
        await Rejects(Cursor("""{"d":"x","k":["2026-01-01T00:00:00",1]}"""), "The cursor does not hold the expected position.");
        await Rejects(Cursor("""{"d":"n","k":["not a date",1]}"""), "The cursor does not hold the expected position.");
        await Rejects(Cursor("""{"d":"n","k":[null,1]}"""), "The cursor holds a null key value.");
    }
}
