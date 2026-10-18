using Naravel.Database;

namespace Naravel.Database.Tests;

/// <summary>
/// The R19 query surface run against every dialect with the same expectations (PDR-022 A6, R20.T04). Each scenario is one theory case so a
/// failure names the scenario and the dialect. SQLite always runs; a server runs only when its <c>NARAVEL_TEST_*</c> variable is set
/// (CI <c>database</c> job, or <c>scripts/test-local.sh --db</c>). Skipped means NOT RUN.
/// </summary>
[Trait("Dialect", "Sqlite")]
public sealed class SqliteContractTests(SqliteContractFixture fixture) : IClassFixture<SqliteContractFixture>
{
    public static IEnumerable<object[]> Scenarios => ContractScenarios.Names(DatabaseDialect.Sqlite);

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task Contract(string scenario) => ContractScenarios.RunAsync(scenario, fixture.Context!);
}

[Trait("Dialect", "MySql")]
public sealed class MySqlContractTests(MySqlContractFixture fixture) : IClassFixture<MySqlContractFixture>
{
    public static IEnumerable<object[]> Scenarios => ContractScenarios.Names(DatabaseDialect.MySql);

    [DialectTheory("NARAVEL_TEST_MYSQL")]
    [MemberData(nameof(Scenarios))]
    public Task Contract(string scenario) => ContractScenarios.RunAsync(scenario, fixture.Context!);
}

[Trait("Dialect", "Postgres")]
public sealed class PostgresContractTests(PostgresContractFixture fixture) : IClassFixture<PostgresContractFixture>
{
    public static IEnumerable<object[]> Scenarios => ContractScenarios.Names(DatabaseDialect.Postgres);

    [DialectTheory("NARAVEL_TEST_POSTGRES")]
    [MemberData(nameof(Scenarios))]
    public Task Contract(string scenario) => ContractScenarios.RunAsync(scenario, fixture.Context!);
}

[Trait("Dialect", "SqlServer")]
public sealed class SqlServerContractTests(SqlServerContractFixture fixture) : IClassFixture<SqlServerContractFixture>
{
    public static IEnumerable<object[]> Scenarios => ContractScenarios.Names(DatabaseDialect.SqlServer);

    [DialectTheory("NARAVEL_TEST_SQLSERVER")]
    [MemberData(nameof(Scenarios))]
    public Task Contract(string scenario) => ContractScenarios.RunAsync(scenario, fixture.Context!);
}
