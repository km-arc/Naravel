using Naravel.Database;

namespace Naravel.Sample.App.Data;

/// <summary>
/// Creates and fills the <c>people</c> table of the in-memory SQLite database the database demo uses.
/// It uses the raw <see cref="IDatabase"/> calls because a query builder only reads; schema belongs to migrations (EF Core) in a real app.
/// </summary>
public sealed class PeopleSeeder
{
    private static readonly (int Id, string Name, int Votes, bool Active, DateTime Born)[] People =
    [
        (1, "Ada", 10, true, new DateTime(2024, 1, 15, 8, 30, 0)),
        (2, "Grace", 20, true, new DateTime(2024, 2, 20, 12, 0, 0)),
        (3, "Linus", 20, false, new DateTime(2023, 1, 15, 23, 59, 59)),
        (4, "Margaret", 30, true, new DateTime(2024, 2, 29, 0, 0, 0)),
        (5, "Dennis", 5, false, new DateTime(2022, 12, 31, 17, 45, 30)),
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _seeded;

    /// <summary>Seeds the table once, or again when <paramref name="reset"/> is true.</summary>
    public async Task EnsureAsync(IDatabase db, bool reset, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_seeded && !reset) return;

            await db.UnpreparedAsync("drop table if exists people", cancellationToken);
            await db.UnpreparedAsync(
                "create table people (id integer primary key, name text not null, votes integer not null, active integer not null, born text not null)",
                cancellationToken);
            foreach (var person in People)
            {
                // Each ? becomes a bound parameter; DateTime and bool are given an explicit DbType by the executor.
                await db.StatementAsync(
                    "insert into people (id, name, votes, active, born) values (?, ?, ?, ?, ?)",
                    [person.Id, person.Name, person.Votes, person.Active, person.Born],
                    cancellationToken);
            }

            _seeded = true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
