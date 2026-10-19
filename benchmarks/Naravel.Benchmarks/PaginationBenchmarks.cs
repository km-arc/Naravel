using BenchmarkDotNet.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Naravel.Pagination;

namespace Naravel.Benchmarks;

public sealed class BenchmarkPost
{
    public int Id { get; set; }

    public string Title { get; set; } = "";
}

public sealed class BenchmarkPostContext(SqliteConnection connection) : DbContext
{
    public DbSet<BenchmarkPost> Posts => Set<BenchmarkPost>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(connection);
}

/// <summary>
/// PDR-020 S2: why keyset paging exists. The same page of 20 rows near the end of a 50 000 row table is read with <c>OFFSET</c> (the database walks and discards every
/// row before the page) and with a cursor (the database seeks straight to the page through the primary key). The first page is included as the control: there the two
/// are expected to be about equal, and the gap must open only as the page moves deeper.
/// </summary>
[MemoryDiagnoser]
public class PaginationBenchmarks
{
    private const int Rows = 50_000;
    private const int PerPage = 20;
    private const int DeepPage = 2_000;

    private SqliteConnection _connection = null!;
    private BenchmarkPostContext _context = null!;
    private string _deepCursor = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _context = new BenchmarkPostContext(_connection);
        await _context.Database.EnsureCreatedAsync();
        for (var i = 1; i <= Rows; i++) _context.Posts.Add(new BenchmarkPost { Id = i, Title = "post-" + i });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        // The cursor that points at the start of DeepPage: read the rows before it once, with a cap raised only for this setup step.
        var skipped = (DeepPage - 1) * PerPage;
        var page = await _context.Posts.AsNoTracking().CursorPaginateAsync(k => k.By(p => p.Id), skipped, null, new PaginationOptions { MaxPerPage = skipped });
        _deepCursor = page.NextCursor!;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Benchmark]
    public Task<Paginated<BenchmarkPost>> OffsetFirstPage() =>
        _context.Posts.AsNoTracking().OrderBy(p => p.Id).PaginateAsync(1, PerPage);

    [Benchmark]
    public Task<SimplePaginated<BenchmarkPost>> SimpleOffsetFirstPage() =>
        _context.Posts.AsNoTracking().OrderBy(p => p.Id).SimplePaginateAsync(1, PerPage);

    [Benchmark]
    public Task<CursorPaginated<BenchmarkPost>> KeysetFirstPage() =>
        _context.Posts.AsNoTracking().CursorPaginateAsync(k => k.By(p => p.Id), PerPage, null);

    [Benchmark(Baseline = true)]
    public Task<SimplePaginated<BenchmarkPost>> SimpleOffsetDeepPage() =>
        _context.Posts.AsNoTracking().OrderBy(p => p.Id).SimplePaginateAsync(DeepPage, PerPage);

    [Benchmark]
    public Task<Paginated<BenchmarkPost>> OffsetDeepPage() =>
        _context.Posts.AsNoTracking().OrderBy(p => p.Id).PaginateAsync(DeepPage, PerPage);

    [Benchmark]
    public Task<CursorPaginated<BenchmarkPost>> KeysetDeepPage() =>
        _context.Posts.AsNoTracking().CursorPaginateAsync(k => k.By(p => p.Id), PerPage, _deepCursor);
}
