using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Database;

namespace Naravel.Database.Tests;

[CollectionDefinition("Database telemetry", DisableParallelization = true)]
public sealed class DatabaseTelemetryCollection;

[Collection("Database telemetry")]
public sealed class DatabaseObservabilityTests
{
    private const string Secret = "s3cret-value-42";

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Default"] = "main",
            ["Database:Connections:main:Driver"] = "sqlite",
            ["Database:Connections:main:Path"] = ":memory:"
        }).Build();
        var services = new ServiceCollection();
        services.AddNaravelDatabase(configuration);
        services.AddNaravelSqliteDatabase(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingListener : IQueryListener
    {
        public ConcurrentQueue<QueryExecuted> Seen { get; } = new();

        public void OnQueryExecuted(QueryExecuted query) => Seen.Enqueue(query);
    }

    private sealed class ThrowingListener : IQueryListener
    {
        public void OnQueryExecuted(QueryExecuted query) => throw new InvalidOperationException("listener bug");
    }

    [Fact]
    public async Task Meter_counts_queries_failures_and_duration_without_sql_or_values_in_tags()
    {
        var tags = new ConcurrentQueue<KeyValuePair<string, object?>>();
        var counts = new ConcurrentDictionary<string, long>();
        var durations = 0;
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "Naravel.Database") meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, measurementTags, _) =>
        {
            counts.AddOrUpdate(instrument.Name, value, (_, current) => current + value);
            foreach (var tag in measurementTags) tags.Enqueue(tag);
        });
        listener.SetMeasurementEventCallback<double>((instrument, _, measurementTags, _) =>
        {
            if (instrument.Name == "naravel.database.query.duration") Interlocked.Increment(ref durations);
            foreach (var tag in measurementTags) tags.Enqueue(tag);
        });
        listener.Start();

        await using var provider = Build();
        var db = provider.GetRequiredService<IDatabase>();
        await db.UnpreparedAsync("create table users (id integer primary key, name text not null)");
        await db.StatementAsync("insert into users (id, name) values (?, ?)", [1, Secret]);
        await db.Table("users").Where("name", Secret).GetAsync();
        var failing = async () => await db.SelectAsync("select * from missing_table where name = ?", [Secret]);
        await failing.Should().ThrowAsync<Exception>();

        counts["naravel.database.queries"].Should().Be(4);
        counts["naravel.database.failures"].Should().Be(1);
        durations.Should().Be(4);
        tags.Should().NotBeEmpty();
        tags.Select(t => t.Key).Distinct().Should().BeEquivalentTo(["connection", "operation"]);
        tags.Select(t => t.Value?.ToString()).Distinct().Should().BeSubsetOf(["main", "select", "statement", "scalar"]);
    }

    [Fact]
    public async Task Activity_carries_connection_and_operation_only()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Naravel.Database",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Enqueue(activity)
        };
        ActivitySource.AddActivityListener(activityListener);

        await using var provider = Build();
        var db = provider.GetRequiredService<IDatabase>();
        await db.UnpreparedAsync("create table users (id integer primary key, name text not null)");
        await db.Table("users").Where("name", Secret).GetAsync();

        activities.Should().HaveCount(2);
        var all = activities.SelectMany(a => a.TagObjects).ToArray();
        all.Select(t => t.Key).Distinct().Should().BeEquivalentTo(["naravel.database.connection", "naravel.database.operation"]);
        all.Select(t => t.Value?.ToString()).Should().NotContain(v => v!.Contains(Secret) || v.Contains("users"));
    }

    [Fact]
    public async Task Listener_receives_sql_bindings_connection_operation_and_duration()
    {
        var recorder = new RecordingListener();
        await using var provider = Build(s => s.AddSingleton<IQueryListener>(recorder));
        var db = provider.GetRequiredService<IDatabase>();
        await db.UnpreparedAsync("create table users (id integer primary key, name text not null)");
        await db.Table("users").Where("name", Secret).GetAsync();

        var query = recorder.Seen.Last();
        query.ConnectionName.Should().Be("main");
        query.Operation.Should().Be(QueryOperation.Select);
        query.Sql.Should().Contain("@p0").And.NotContain(Secret);
        query.Bindings.Should().Equal(Secret);
        query.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
        query.Exception.Should().BeNull();
    }

    [Fact]
    public async Task Listener_sees_the_failure_and_a_throwing_listener_does_not_break_the_query()
    {
        var recorder = new RecordingListener();
        await using var provider = Build(s =>
        {
            s.AddSingleton<IQueryListener, ThrowingListener>();
            s.AddSingleton<IQueryListener>(recorder);
        });
        var db = provider.GetRequiredService<IDatabase>();

        var failing = async () => await db.SelectAsync("select * from missing_table");
        var thrown = await failing.Should().ThrowAsync<Exception>();

        thrown.Which.Message.Should().NotContain("listener bug");
        recorder.Seen.Should().ContainSingle().Which.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Query_log_is_opt_in_bounded_clearable_and_can_be_disabled()
    {
        await using var provider = Build(s => s.AddNaravelDatabaseQueryLog(maxEntries: 2));
        var db = provider.GetRequiredService<IDatabase>();
        var log = provider.GetRequiredService<QueryLog>();

        await db.UnpreparedAsync("create table users (id integer primary key, name text not null)");
        await db.StatementAsync("insert into users (id, name) values (?, ?)", [1, "Ada"]);
        await db.StatementAsync("insert into users (id, name) values (?, ?)", [2, "Grace"]);
        log.Entries.Should().HaveCount(2);
        log.Entries[0].Bindings.Should().Equal(1, "Ada");
        log.Entries[1].Bindings.Should().Equal(2, "Grace");

        log.Clear();
        log.Entries.Should().BeEmpty();

        log.Enabled = false;
        await db.Table("users").GetAsync();
        log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_registering_the_log_nothing_is_recorded_and_no_log_service_exists()
    {
        await using var provider = Build();
        provider.GetService<QueryLog>().Should().BeNull();
        await provider.GetRequiredService<IDatabase>().UnpreparedAsync("create table t (id integer)");
    }
}
