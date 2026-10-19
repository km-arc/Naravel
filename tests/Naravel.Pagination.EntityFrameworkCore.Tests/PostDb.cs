using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Naravel.Pagination.EntityFrameworkCore.Tests;

public sealed class Post
{
    public int Id { get; set; }

    public string Title { get; set; } = "";

    public int Bucket { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? Score { get; set; }

    public Guid Token { get; set; }
}

public sealed class PostContext(SqliteConnection connection) : DbContext
{
    public DbSet<Post> Posts => Set<Post>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
}

/// <summary>An in-memory SQLite database that lives as long as the test; the connection stays open so the data does.</summary>
public sealed class PostDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public PostDb(int count = 0)
    {
        _connection.Open();
        Context = new PostContext(_connection);
        Context.Database.EnsureCreated();
        for (var i = 1; i <= count; i++)
        {
            Context.Posts.Add(new Post { Id = i, Title = $"post-{i:000}", Bucket = i % 3, CreatedAt = new DateTime(2026, 1, 1).AddDays(i), Score = i });
        }

        Context.SaveChanges();
    }

    public PostContext Context { get; }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }
}
