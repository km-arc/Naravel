using Naravel.Database;

namespace Naravel.Database.Tests;

public sealed class ContractPerson
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public long Votes { get; set; }

    public bool Active { get; set; }

    public string? Note { get; set; }
}

public enum ContractColor
{
    Red = 3,
    Blue = 7
}

/// <summary>The scenarios of the contract suite. Seed data: Ada 10 active, Grace 20 active, Linus 20 inactive, Margaret 30 active, Dennis 5 inactive (ids 1 to 5).</summary>
internal static class ContractScenarios
{
    private sealed record Scenario(string Name, Func<ContractContext, Task> Run, params DatabaseDialect[] NotSupportedOn);

    private static readonly Scenario[] All =
    [
        new("select_all_ordered", async c => (await NamesAsync(c.Db.Table(c.People).OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Margaret", "Dennis")),
        new("where_equals_and_operator", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).Where("votes", 20).OrderBy("id"))).Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.People).Where("votes", ">", 10).Where("active", true).OrderBy("id"))).Should().Equal("Grace", "Margaret");
        }),
        new("where_or_not_and_groups", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).Where("votes", "<", 10).OrWhere("votes", ">=", 30).OrderBy("id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereNot("votes", 20).OrderBy("id"))).Should().Equal("Ada", "Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).Where(q => q.Where("votes", 20).OrWhere("votes", 5)).Where("active", false).OrderBy("id")))
                .Should().Equal("Linus", "Dennis");
        }),
        new("where_in_and_not_in", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereIn("id", new[] { 1, 3, 5 }).OrderBy("id"))).Should().Equal("Ada", "Linus", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereNotIn("id", new[] { 1, 3, 5 }).OrderBy("id"))).Should().Equal("Grace", "Margaret");
        }),
        new("where_raw_null_check", async c =>
            (await NamesAsync(c.Db.Table(c.People).WhereRaw("note is null").OrderBy("id"))).Should().Equal("Grace", "Margaret")),
        new("where_between_and_columns", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereBetween("votes", 10, 20).OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.People).WhereNotBetween("votes", 10, 20).OrderBy("id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereNotBetweenColumns("votes", "id", "id").OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).WhereBetweenColumns("votes", "id", "id").OrderBy("id"))).Should().Equal("Dennis");
        }),
        new("where_null_and_column", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereNull("note").OrderBy("id"))).Should().Equal("Grace", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).WhereNotNull("note").OrderBy("id"))).Should().Equal("Ada", "Linus", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereColumn("votes", "id").OrderBy("id"))).Should().Equal("Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereNull("note").OrWhereColumn("votes", "<", "id").OrderBy("id"))).Should().Equal("Grace", "Margaret");
        }),
        new("where_date_parts", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereDate("born", new DateOnly(2024, 2, 20)).OrderBy("id"))).Should().Equal("Grace");
            (await NamesAsync(c.Db.Table(c.People).WhereDate("born", ">=", "2024-01-01").OrderBy("id"))).Should().Equal("Ada", "Grace", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).WhereYear("born", 2024).OrderBy("id"))).Should().Equal("Ada", "Grace", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).WhereMonth("born", 2).OrderBy("id"))).Should().Equal("Grace", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).WhereDay("born", 15).OrderBy("id"))).Should().Equal("Ada", "Linus");
            (await NamesAsync(c.Db.Table(c.People).WhereYear("born", 2022).OrWhereMonth("born", 2).OrderBy("id"))).Should().Equal("Grace", "Margaret", "Dennis");
        }),
        new("where_time_part", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereTime("born", ">", new TimeOnly(12, 0, 0)).OrderBy("id"))).Should().Equal("Linus", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereTime("born", new TimeOnly(12, 0, 0)).OrderBy("id"))).Should().Equal("Grace");
            (await NamesAsync(c.Db.Table(c.People).WhereTime("born", "<", "09:00").OrderBy("id"))).Should().Equal("Ada", "Margaret");
        }),
        new("where_any_all_none", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereAny(["votes", "id"], 20).OrderBy("id"))).Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.People).WhereAny(["votes", "id"], 3).OrderBy("id"))).Should().Equal("Linus");
            (await NamesAsync(c.Db.Table(c.People).WhereAll(["votes", "id"], ">", 4).OrderBy("id"))).Should().Equal("Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereNone(["votes", "id"], ">", 20).OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 1).OrWhereAll(["votes", "id"], ">", 4).OrderBy("id"))).Should().Equal("Ada", "Dennis");
        }),
        new("where_or_variants_of_the_new_where_methods", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).Where("id", 5).OrWhereBetween("votes", 30, 30).OrderBy("id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 1).OrWhereNotBetween("votes", 5, 29).OrderBy("id"))).Should().Equal("Ada", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 1).OrWhereNull("note").OrderBy("id"))).Should().Equal("Ada", "Grace", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 2).OrWhereNotNull("note").OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 1).OrWhereColumn("id", "votes").OrderBy("id"))).Should().Equal("Ada", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereColumn(new Dictionary<string, string> { ["id"] = "votes" }).OrderBy("id"))).Should().Equal("Dennis");
        }),
        new("where_like_ignores_case", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereLike("name", "%A%").OrderBy("id"))).Should().Equal("Ada", "Grace");
            (await NamesAsync(c.Db.Table(c.Docs).WhereNotLike("name", "%a%").OrderBy("id"))).Should().Equal("Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereLike("name", "L%").OrWhereLike("name", "ada").OrderBy("id"))).Should().Equal("Ada", "Linus");
        }),
        new("where_like_case_sensitive", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereLike("name", "%A%", caseSensitive: true).OrderBy("id"))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(c.Docs).WhereNotLike("name", "%A%", caseSensitive: true).OrderBy("id"))).Should().Equal("Grace", "Linus");
        }, DatabaseDialect.Sqlite),
        new("where_json_path_compares_text_and_numbers", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).Where("payload->name", "Grace"))).Should().Equal("Grace");
            (await NamesAsync(c.Db.Table(c.Docs).Where("payload->age", ">", 40).OrderBy("id"))).Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereIn("payload->name", new[] { "Ada", "Linus" }).OrderBy("id"))).Should().Equal("Ada", "Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereBetween("payload->age", 40, 50).OrderBy("id"))).Should().Equal("Grace");
        }),
        new("where_json_path_null_and_missing_key", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereNotNull("payload->nick"))).Should().Equal("Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereNull("payload->nick").OrderBy("id"))).Should().Equal("Ada", "Grace");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonContainsKey("payload->nick"))).Should().Equal("Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonDoesntContainKey("payload->nick").OrderBy("id"))).Should().Equal("Ada", "Grace");
        }),
        new("where_json_contains_a_scalar_in_an_array", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonContains("payload->tags", "php"))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonDoesntContain("payload->tags", "php").OrderBy("id"))).Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonContains("payload->tags", "cobol").OrWhereJsonContains("payload->tags", "git").OrderBy("id"))).Should().Equal("Grace", "Linus");
        }),
        new("where_json_length_of_an_array", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonLength("payload->tags", ">", 1).OrderBy("id"))).Should().Equal("Ada", "Linus");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonLength("payload->tags", 1))).Should().Equal("Grace");
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonLength("payload->tags", 3).OrWhereJsonLength("payload->tags", "<", 2).OrderBy("id"))).Should().Equal("Grace", "Linus");
        }),
        new("where_json_boolean_value", async c =>
            (await NamesAsync(c.Db.Table(c.Docs).Where("payload->active", true).OrderBy("id"))).Should().Equal("Ada", "Linus"), DatabaseDialect.MySql),
        new("where_json_contains_a_whole_array", async c =>
        {
            (await NamesAsync(c.Db.Table(c.Docs).WhereJsonContains("payload->tags", new[] { "c", "git" }))).Should().Equal("Linus");
        }, DatabaseDialect.Sqlite, DatabaseDialect.SqlServer),
        new("where_full_text_natural", async c =>
            (await NamesAsync(c.Db.Table(c.Docs).WhereFullText("body", "kernel").OrderBy("id"))).Should().Equal("Ada", "Linus"), DatabaseDialect.Sqlite, DatabaseDialect.SqlServer),
        new("where_full_text_is_unsupported_on_sqlite", async c =>
            await c.Db.Table(c.Docs).WhereFullText("body", "kernel").Awaiting(q => q.GetAsync()).Should().ThrowAsync<NotSupportedException>(),
            DatabaseDialect.MySql, DatabaseDialect.Postgres, DatabaseDialect.SqlServer),
        new("where_exists_and_subqueries", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereExists(s => s.Table(c.Docs).WhereColumn($"{c.Docs}.id", $"{c.People}.id").Where("name", "Ada")))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(c.People).WhereNotExists(s => s.Table(c.Docs).WhereColumn($"{c.Docs}.id", $"{c.People}.id")).OrderBy("id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereIn("id", s => s.Table(c.Docs).Select("id").Where("name", "Grace")))).Should().Equal("Grace");
            (await NamesAsync(c.Db.Table(c.People).WhereNotIn("id", s => s.Table(c.Docs).Select("id")).OrderBy("id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(c.People).WhereSubquery("votes", ">", s => s.Table(c.People).Select("votes").Where("name", "Ada")).OrderBy("id"))).Should().Equal("Grace", "Linus", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).Where("id", 5).OrWhereExists(s => s.Table(c.Docs).Where("name", "Linus").Where("id", 3)).OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Margaret", "Dennis");
        }),
        new("when_and_unless", async c =>
            (await NamesAsync(c.Db.Table(c.People)
                .When(true, q => q.Where("votes", 20))
                .Unless(true, q => q.Where("id", 1), q => q.Where("active", true))
                .OrderBy("id"))).Should().Equal("Grace")),
        new("joins_inner_left_right_cross_and_alias", async c =>
        {
            var (people, docs) = (c.People, c.Docs);
            (await NamesAsync(c.Db.Table(people).Join(docs, $"{people}.id", $"{docs}.id").Select($"{people}.name").OrderBy($"{people}.id"))).Should().Equal("Ada", "Grace", "Linus");
            (await NamesAsync(c.Db.Table(people).LeftJoin(docs, $"{people}.id", $"{docs}.id").WhereNull($"{docs}.id").Select($"{people}.name").OrderBy($"{people}.id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(docs).RightJoin(people, $"{people}.id", $"{docs}.id").WhereNull($"{docs}.id").Select($"{people}.name").OrderBy($"{people}.id"))).Should().Equal("Margaret", "Dennis");
            (await NamesAsync(c.Db.Table(people).CrossJoin(docs).Where($"{docs}.id", 1).Select($"{people}.name").OrderBy($"{people}.id"))).Should().Equal("Ada", "Grace", "Linus", "Margaret", "Dennis");
            (await NamesAsync(c.Db.Table($"{people} as p").Join($"{docs} as d", "p.id", "d.id").Where("d.name", "Grace").Select("p.name"))).Should().Equal("Grace");
        }),
        new("join_closure_with_on_or_on_and_values", async c =>
        {
            var (people, docs) = (c.People, c.Docs);
            (await NamesAsync(c.Db.Table(people).Join(docs, j => j.On($"{people}.id", $"{docs}.id").Where($"{docs}.name", "Ada")).Select($"{people}.name"))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(people).Join(docs, j => j.On($"{people}.id", $"{docs}.id").Where($"{docs}.name", "<>", "Ada")).Select($"{people}.name").OrderBy($"{people}.id")))
                .Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(people).Join(docs, j => j.On($"{people}.id", $"{docs}.id").Where($"{docs}.name", "Ada")).Where($"{people}.votes", 10).Select($"{people}.name"))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(people).LeftJoin(docs, j => j.On($"{people}.id", $"{docs}.id").OrOn($"{people}.votes", "<", $"{docs}.id")).Where($"{docs}.name", "Grace").Select($"{people}.name"))).Should().Equal("Grace");
        }),
        new("group_by_with_having_on_grouped_columns", async c =>
        {
            var votes = (await c.Db.Table(c.People).Select("votes").GroupBy("votes").Having("votes", ">", 5).OrderBy("votes").GetAsync()).Select(r => Convert.ToInt64(r["votes"])).ToArray();
            votes.Should().Equal(10L, 20L, 30L);
            var between = (await c.Db.Table(c.People).Select("votes").GroupBy("votes").HavingBetween("votes", 10, 20).OrHaving("votes", 30).OrderBy("votes").GetAsync()).Select(r => Convert.ToInt64(r["votes"])).ToArray();
            between.Should().Equal(10L, 20L, 30L);
            (await c.Db.Table(c.People).Select("active").GroupBy("active").HavingNotNull("active").GetAsync()).Should().HaveCount(2);
        }),
        new("group_by_with_having_on_a_select_alias", async c =>
        {
            var rows = await c.Db.Table(c.People).SelectRaw("votes, count(*) as n").GroupBy("votes").Having("n", ">", 1).GetAsync();
            rows.Should().ContainSingle();
            Convert.ToInt64(rows[0]["votes"]).Should().Be(20);
        }, DatabaseDialect.Postgres, DatabaseDialect.SqlServer),
        new("latest_oldest_reorder_and_random", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).Latest("id").Limit(2))).Should().Equal("Dennis", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).Oldest("id").Limit(2))).Should().Equal("Ada", "Grace");
            (await NamesAsync(c.Db.Table(c.People).OrderBy("id").Reorder().OrderByDesc("votes").OrderBy("id").Limit(1))).Should().Equal("Margaret");
            (await NamesAsync(c.Db.Table(c.People).OrderBy("id").ReorderDesc("id").Limit(1))).Should().Equal("Dennis");
            (await NamesAsync(c.Db.Table(c.People).Reorder("id").Limit(1))).Should().Equal("Ada");
            (await NamesAsync(c.Db.Table(c.People).InRandomOrder())).Should().BeEquivalentTo("Ada", "Grace", "Linus", "Margaret", "Dennis");
        }),
        new("where_in_with_an_empty_list", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).WhereIn("id", Array.Empty<int>()))).Should().BeEmpty();
            (await NamesAsync(c.Db.Table(c.People).WhereNotIn("id", Array.Empty<int>()).OrderBy("id"))).Should().Equal("Ada", "Grace", "Linus", "Margaret", "Dennis");
        }),
        new("order_desc_limit_offset", async c =>
        {
            (await NamesAsync(c.Db.Table(c.People).OrderBy("id").Limit(2).Offset(1))).Should().Equal("Grace", "Linus");
            (await NamesAsync(c.Db.Table(c.People).OrderByDesc("id").Take(2))).Should().Equal("Dennis", "Margaret");
            (await NamesAsync(c.Db.Table(c.People).OrderBy("id").Skip(3).Take(10))).Should().Equal("Margaret", "Dennis");
        }),
        new("select_columns_and_distinct", async c =>
        {
            var rows = await c.Db.Table(c.People).Select("name").OrderBy("id").Limit(1).GetAsync();
            rows.Should().ContainSingle().Which.Keys.Should().BeEquivalentTo(["name"]);
            var votes = await c.Db.Table(c.People).Select("votes").Distinct().OrderBy("votes").GetAsync();
            votes.Select(r => Convert.ToInt64(r["votes"])).Should().Equal(5L, 10L, 20L, 30L);
        }),
        new("first_find_sole_value_pluck", async c =>
        {
            (await c.Db.Table(c.People).OrderByDesc("id").FirstAsync())!["name"].Should().Be("Dennis");
            (await c.Db.Table(c.People).Where("name", "nobody").FirstAsync()).Should().BeNull();
            (await c.Db.Table(c.People).FindAsync(3))!["name"].Should().Be("Linus");
            (await c.Db.Table(c.People).Where("votes", 30).SoleAsync())["name"].Should().Be("Margaret");
            await c.Db.Table(c.People).Where("votes", 999).Awaiting(q => q.SoleAsync()).Should().ThrowAsync<RecordsNotFoundException>();
            await c.Db.Table(c.People).Where("votes", 20).Awaiting(q => q.SoleAsync()).Should().ThrowAsync<MultipleRecordsFoundException>();
            (await c.Db.Table(c.People).Where("id", 2).ValueAsync<string>("name")).Should().Be("Grace");
            (await c.Db.Table(c.People).Where("id", 99).ValueAsync<string>("name")).Should().BeNull();
            (await c.Db.Table(c.People).OrderBy("id").PluckAsync<string>("name")).Should().Equal("Ada", "Grace", "Linus", "Margaret", "Dennis");
        }),
        new("typed_rows_with_dapper", async c =>
        {
            var people = await c.Db.Table(c.People).Where("votes", 20).OrderBy("id").GetAsync<ContractPerson>();
            people.Select(p => p.Name).Should().Equal("Grace", "Linus");
            people.Select(p => p.Active).Should().Equal(true, false);
            people[0].Note.Should().BeNull();
            people[1].Note.Should().Be("kernel");
            var first = await c.Db.Table(c.People).Where("id", 1).FirstAsync<ContractPerson>();
            first.Votes.Should().Be(10);
            first.Active.Should().BeTrue();
        }),
        new("null_column_is_returned_as_null", async c =>
            (await c.Db.Table(c.People).Where("id", 2).FirstAsync())!["note"].Should().BeNull()),
        new("raw_select_scalar_and_placeholders", async c =>
        {
            var rows = await c.Db.SelectAsync($"select name from {c.People} where votes > ? and active = ? order by id", [10, true]);
            rows.Select(r => (string)r["name"]!).Should().Equal("Grace", "Margaret");
            (await c.Db.ScalarAsync<long>($"select count(*) from {c.People}")).Should().Be(5);
            (await c.Db.ScalarAsync<string>("select 'a??b'")).Should().Be("a?b");
        }),
        new("raw_statements_report_affected_rows", async c =>
        {
            // Works on its own row so a failure here can never change the seed data the other scenarios read.
            await c.Db.StatementAsync($"insert into {c.People} (id, name, votes, active, born) values (?, ?, ?, ?, ?)", [100, "Temp", 1, true, new DateTime(2000, 1, 1, 0, 0, 0)]);
            (await c.Db.AffectingStatementAsync($"update {c.People} set votes = votes + ? where id = ?", [1, 100])).Should().Be(1);
            (await c.Db.Table(c.People).Where("id", 100).ValueAsync<long>("votes")).Should().Be(2);
            (await c.Db.AffectingStatementAsync($"delete from {c.People} where id = ?", [100])).Should().Be(1);
            (await c.Db.AffectingStatementAsync($"delete from {c.People} where id = ?", [100])).Should().Be(0);
        }),
        new("wrong_binding_count_is_rejected", async c =>
            await c.Db.Awaiting(db => db.SelectAsync($"select * from {c.People} where id = ?", [1, 2])).Should().ThrowAsync<ArgumentException>()),

        // Type round trip (PDR-022 A2): each type is written as a parameter and read back through the typed forms.
        new("type_bool", c => RoundTripAsync(c, 1, "c_bool", true)),
        new("type_int64", c => RoundTripAsync(c, 2, "c_long", 9_000_000_000L)),
        new("type_double", c => RoundTripAsync(c, 3, "c_double", 3.5d)),
        new("type_decimal", c => RoundTripAsync(c, 4, "c_decimal", 12345.6789m)),
        new("type_string_unicode", c => RoundTripAsync(c, 5, "c_string", "naravel سلام")),
        new("type_guid", c => RoundTripAsync(c, 6, "c_guid", Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"))),
        new("type_datetime", c => RoundTripAsync(c, 7, "c_datetime", new DateTime(2026, 10, 18, 12, 30, 45, DateTimeKind.Unspecified))),
        new("type_datetimeoffset", c => RoundTripAsync(c, 8, "c_offset", new DateTimeOffset(2026, 10, 18, 12, 30, 45, TimeSpan.Zero)), DatabaseDialect.MySql),
        new("type_dateonly", c => RoundTripAsync(c, 9, "c_date", new DateOnly(2026, 10, 18))),
        new("type_timeonly", c => RoundTripAsync(c, 10, "c_time", new TimeOnly(12, 30, 45))),
        new("type_bytes", c => RoundTripAsync(c, 11, "c_bytes", new byte[] { 1, 2, 3, 250 })),
        new("type_enum_as_underlying_integer", async c =>
        {
            await c.Db.StatementAsync($"insert into {c.Types} (id, c_enum) values (?, ?)", [12, ContractColor.Blue]);
            (await c.Db.Table(c.Types).Where("id", 12).ValueAsync<int>("c_enum")).Should().Be(7);
        }),
        new("type_null_round_trip", async c =>
        {
            await c.Db.StatementAsync($"insert into {c.Types} (id, c_string, c_decimal, c_guid) values (?, ?, ?, ?)", [13, null, null, null]);
            var row = await c.Db.Table(c.Types).Where("id", 13).FirstAsync();
            row!["c_string"].Should().BeNull();
            row["c_decimal"].Should().BeNull();
            row["c_guid"].Should().BeNull();
        })
    ];

    public static IEnumerable<object[]> Names(DatabaseDialect dialect) =>
        All.Where(s => !s.NotSupportedOn.Contains(dialect)).Select(s => new object[] { s.Name });

    public static Task RunAsync(string name, ContractContext context) => All.Single(s => s.Name == name).Run(context);

    private static async Task<string[]> NamesAsync(Query query) =>
        (await query.GetAsync()).Select(row => (string)row["name"]!).ToArray();

    private static async Task RoundTripAsync<T>(ContractContext context, int id, string column, T value)
    {
        await context.Db.StatementAsync($"insert into {context.Types} (id, {column}) values (?, ?)", [id, value]);
        var read = await context.Db.Table(context.Types).Where("id", id).ValueAsync<T>(column);
        read.Should().BeEquivalentTo(value);
    }
}
