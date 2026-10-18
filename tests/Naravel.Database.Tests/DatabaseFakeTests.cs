using Naravel.Database;
using Naravel.Database.Testing;

namespace Naravel.Database.Tests;

public sealed class DatabaseFakeTests
{
    [Fact]
    public async Task Table_queries_compile_real_sql_and_are_recorded()
    {
        var fake = new DatabaseFake(DatabaseDialect.Postgres);

        await fake.Table("users").Where("votes", ">", 5).GetAsync();

        var executed = fake.AssertExecuted();
        executed.Sql.Should().Be("select * from \"users\" where \"votes\" > @p0");
        executed.Bindings.Should().Equal(5);
        executed.Operation.Should().Be(QueryOperation.Select);
    }

    [Fact]
    public async Task Canned_rows_are_returned_by_matching_sql_and_the_latest_setup_wins()
    {
        var fake = new DatabaseFake()
            .ReturnRows([new Dictionary<string, object?> { ["id"] = 1 }])
            .ReturnRows([new Dictionary<string, object?> { ["id"] = 99 }], sqlContains: "from \"orders\"");

        var users = await fake.Table("users").GetAsync();
        var orders = await fake.Table("orders").GetAsync();

        users.Should().ContainSingle().Which["ID"].Should().Be(1);
        orders.Should().ContainSingle().Which["id"].Should().Be(99);
    }

    [Fact]
    public async Task Canned_objects_serve_mapped_reads_and_first()
    {
        var fake = new DatabaseFake().ReturnObjects([new UserRow { Id = 7, Name = "Ada" }]);

        var all = await fake.Table("users").GetAsync<UserRow>();
        var first = await fake.Table("users").FirstAsync<UserRow>();
        var raw = await fake.SelectAsync<UserRow>("select * from users");

        all.Should().ContainSingle().Which.Name.Should().Be("Ada");
        first.Name.Should().Be("Ada");
        raw.Should().ContainSingle();
        fake.Executed.Should().HaveCount(3);
    }

    [Fact]
    public async Task Scalar_and_affected_results_are_converted_and_default_to_empty()
    {
        var fake = new DatabaseFake().ReturnScalar(3L, "users").ReturnAffected(5, "update");

        (await fake.ScalarAsync<int>("select count(*) from users")).Should().Be(3);
        (await fake.Table("users").ValueAsync<long>("votes")).Should().Be(3);
        (await fake.AffectingStatementAsync("update users set votes = ?", [1])).Should().Be(5);
        (await fake.ScalarAsync<string>("select name from other")).Should().BeNull();
        (await fake.AffectingStatementAsync("delete from users")).Should().Be(0);
        (await fake.SelectAsync("select 1")).Should().BeEmpty();
    }

    [Fact]
    public async Task Connection_shares_the_record_and_reports_its_name()
    {
        var fake = new DatabaseFake();

        await fake.Connection("reports").StatementAsync("insert into t values (?)", [1]);

        var executed = fake.AssertExecuted(q => q.ConnectionName == "reports");
        executed.Operation.Should().Be(QueryOperation.Statement);
        fake.Connection("reports").ConnectionName.Should().Be("reports");
    }

    [Fact]
    public async Task Raw_sql_placeholders_are_converted_like_the_real_database()
    {
        var fake = new DatabaseFake();

        await fake.StatementAsync("update t set a = ? where b = ?", [1, 2]);

        fake.AssertExecuted().Sql.Should().Be("update t set a = @p0 where b = @p1");
        var wrongCount = async () => await fake.StatementAsync("update t set a = ?", [1, 2]);
        await wrongCount.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Assertions_pass_and_fail_with_helpful_messages()
    {
        var fake = new DatabaseFake();
        fake.AssertNothingExecuted();
        var none = () => fake.AssertExecuted();
        none.Should().Throw<InvalidOperationException>().WithMessage("*none did*");

        await fake.Table("users").GetAsync();

        fake.AssertExecutedCount(1);
        var wrongCount = () => fake.AssertExecutedCount(2);
        wrongCount.Should().Throw<InvalidOperationException>().WithMessage("*Expected 2*found 1*select * from \"users\"*");
        var noMatch = () => fake.AssertExecuted(q => q.Sql.Contains("orders"));
        noMatch.Should().Throw<InvalidOperationException>().WithMessage("*No database command matched*");
        var notNothing = fake.AssertNothingExecuted;
        notNothing.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task A_cancelled_token_is_honoured()
    {
        var fake = new DatabaseFake();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var run = async () => await fake.Table("users").GetAsync(cts.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        fake.AssertNothingExecuted();
    }
}
