using Microsoft.AspNetCore.Mvc;
using Naravel.Database;
using Naravel.Sample.App.Data;

namespace Naravel.Sample.App.Http.Controllers;

/// <summary>
/// Shows the Naravel query builder end to end: conditions are added only when a request parameter is present, every value is a bound
/// parameter, and a column name that is not a plain identifier is rejected when the method is called.
/// </summary>
[ApiController]
[Route("database")]
public sealed class DatabaseDemoController : ControllerBase
{
    private readonly IDatabase _db;
    private readonly PeopleSeeder _seeder;
    private readonly QueryLog _log;
    private readonly IWebHostEnvironment _environment;

    public DatabaseDemoController(IDatabase db, PeopleSeeder seeder, QueryLog log, IWebHostEnvironment environment)
    {
        _db = db;
        _seeder = seeder;
        _log = log;
        _environment = environment;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var viewPath = Path.Combine(_environment.ContentRootPath, "resources", "views", "database", "index.html");
        return PhysicalFile(viewPath, "text/html");
    }

    /// <summary>Drops and recreates the <c>people</c> table with five rows.</summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken cancellationToken)
    {
        await _seeder.EnsureAsync(_db, reset: true, cancellationToken);
        _log.Clear();
        return Ok(new { seeded = 5 });
    }

    /// <summary>
    /// Builds <c>select * from people ...</c> from the query string. The response shows the SQL with its <c>@p</c> markers and the
    /// bound values next to the rows, so you can see that a value such as <c>name=x' or '1'='1</c> never becomes SQL.
    /// </summary>
    [HttpGet("people")]
    public async Task<IActionResult> People(
        [FromQuery] string? name,
        [FromQuery] int? minVotes,
        [FromQuery] bool? active,
        [FromQuery] int? bornYear,
        [FromQuery] string orderBy = "id",
        [FromQuery] bool desc = false,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        await _seeder.EnsureAsync(_db, reset: false, cancellationToken);

        try
        {
            var query = _db.Table("people");
            if (!string.IsNullOrEmpty(name)) query = query.Where("name", "like", $"%{name}%");
            if (minVotes is { } votes) query = query.Where("votes", ">=", votes);
            if (active is { } isActive) query = query.Where("active", isActive);
            if (bornYear is { } year) query = query.WhereYear("born", year);

            // The column name comes from the caller, so the builder validates it: "id; drop table people" throws ArgumentException here.
            query = query.OrderBy(orderBy, desc ? "desc" : "asc").Limit(Math.Clamp(limit, 1, 100));

            var rows = await query.GetAsync(cancellationToken);
            return Ok(new { sql = query.ToSql(), bindings = query.GetBindings(), rows });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    /// <summary>One row by key. Laravel: <c>DB::table('people')->find($id)</c>.</summary>
    [HttpGet("people/{id:int}")]
    public async Task<IActionResult> Person(int id, CancellationToken cancellationToken)
    {
        await _seeder.EnsureAsync(_db, reset: false, cancellationToken);
        var row = await _db.Table("people").FindAsync(id, "id", cancellationToken);
        if (row is null) return NotFound();
        return Ok(row);
    }

    /// <summary>The queries the demo ran so far, from the opt-in <see cref="QueryLog"/>.</summary>
    [HttpGet("log")]
    public IActionResult Log() => Ok(_log.Entries.Select(entry => new
    {
        connection = entry.ConnectionName,
        operation = entry.Operation.ToString(),
        sql = entry.Sql,
        bindings = entry.Bindings,
        durationMs = Math.Round(entry.Duration.TotalMilliseconds, 3),
        failed = entry.Exception is not null,
    }));
}
