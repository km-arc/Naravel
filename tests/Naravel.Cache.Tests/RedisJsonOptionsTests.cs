using System.Text.Json;
using System.Text.Json.Serialization;
using Naravel.Cache.Redis;
using Naravel.Testing;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

public sealed record RedisCachedProfile(string Name, int Visits);

public sealed record RedisUncachedType(string Value);

[JsonSerializable(typeof(RedisCachedProfile))]
internal sealed partial class RedisCacheJsonContext : JsonSerializerContext;

/// <summary>The store's JSON options (trimming and Native AOT): a source-generated resolver round-trips its types and refuses the others loudly.</summary>
public sealed class RedisJsonOptionsTests
{
    private static JsonSerializerOptions SourceGeneratedOptions() => new() { TypeInfoResolver = RedisCacheJsonContext.Default };

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_source_generated_resolver_round_trips_a_type_without_reflection()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        using var store = new RedisCacheStore("json", multiplexer, "naravel-json:" + Guid.NewGuid().ToString("N"), serializerOptions: SourceGeneratedOptions());

        await store.SetAsync("p", new RedisCachedProfile("Ada", 3), TimeSpan.FromMinutes(1));
        var (found, value) = await store.TryGetAsync<RedisCachedProfile>("p");

        found.Should().BeTrue();
        value.Should().Be(new RedisCachedProfile("Ada", 3));
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task A_type_the_resolver_does_not_know_fails_loudly_instead_of_being_dropped()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        using var store = new RedisCacheStore("json", multiplexer, "naravel-json:" + Guid.NewGuid().ToString("N"), serializerOptions: SourceGeneratedOptions());

        var act = () => store.SetAsync("u", new RedisUncachedType("x"), TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [ServiceFact("NARAVEL_TEST_REDIS")]
    public async Task Without_options_the_format_is_unchanged_default_reflection_json()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(Environment.GetEnvironmentVariable("NARAVEL_TEST_REDIS")!);
        var prefix = "naravel-json:" + Guid.NewGuid().ToString("N");
        using var store = new RedisCacheStore("json", multiplexer, prefix);

        await store.SetAsync("p", new RedisCachedProfile("Ada", 3), TimeSpan.FromMinutes(1));

        var raw = (string?)await multiplexer.GetDatabase().StringGetAsync(prefix + ":p");
        raw.Should().Be("""{"Name":"Ada","Visits":3}""");
        (await store.TryGetAsync<RedisCachedProfile>("p")).Value.Should().Be(new RedisCachedProfile("Ada", 3));
    }
}
