using System.Reflection;
using System.Net;
using System.Runtime.CompilerServices;
using Naravel.Cache.Redis;
using StackExchange.Redis;

namespace Naravel.Cache.Tests;

public sealed class RedisProviderTests
{
    [Theory]
    [InlineData("naravel", "naravel:*")]
    [InlineData("app[one]", "app\\[one\\]:*")]
    [InlineData("tenant*", "tenant\\*:*" )]
    public void Redis_flush_pattern_escapes_prefix_globs(string prefix, string expected)
    {
        RedisCacheStore.BuildScanPattern(prefix).Should().Be(expected);
    }

    [Fact]
    public async Task Redis_flush_scans_and_deletes_only_the_configured_store_prefix()
    {
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisFlushMultiplexerProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisFlushDatabaseProxy>();
        var server = DispatchProxy.Create<IServer, RedisFlushServerProxy>();
        var muxProxy = (RedisFlushMultiplexerProxy)(object)multiplexer;
        var databaseProxy = (RedisFlushDatabaseProxy)(object)database;
        var serverProxy = (RedisFlushServerProxy)(object)server;
        muxProxy.Database = database;
        muxProxy.Server = server;
        serverProxy.Keys = ["app[one]:owned", "another-store:keep", "unrelated:keep"];
        using var store = new RedisCacheStore("one", multiplexer, "app[one]");

        await store.FlushAsync();

        serverProxy.LastPattern.Should().Be("app\\[one\\]:*");
        databaseProxy.DeletedKeys.Should().Equal("app[one]:owned");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(-300_000L)]
    public async Task Redis_set_with_a_ttl_of_zero_or_less_deletes_the_key_and_never_sets_it(long milliseconds)
    {
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisFlushMultiplexerProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisFlushDatabaseProxy>();   // it throws for a StringSet, which this must not send
        ((RedisFlushMultiplexerProxy)(object)multiplexer).Database = database;
        using var store = new RedisCacheStore("one", multiplexer, "app");

        await store.SetAsync("k", "value", TimeSpan.FromMilliseconds(milliseconds));

        ((RedisFlushDatabaseProxy)(object)database).DeletedKeys.Should().Equal("app:k");
    }

    [Fact]
    public async Task Redis_lock_uses_atomic_token_checked_release_script()
    {
        var mux = DispatchProxy.Create<IConnectionMultiplexer, CacheProviderDispatchProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        var multiplexer = (CacheProviderDispatchProxy)(object)mux;
        multiplexer.NextResult = database;
        var proxy = (RedisDatabaseProxy)(object)database;
        proxy.ScriptResult = 1L;
        using var cacheLock = new RedisLock(mux, "tests");

        var acquired = await cacheLock.AcquireAsync("order", TimeSpan.FromSeconds(5));
        acquired.Should().NotBeNull();
        (await cacheLock.ReleaseAsync("order", acquired!)).Should().BeTrue();
        proxy.LastScript.Should().Contain("redis.call('get', KEYS[1]) == ARGV[1]");
    }

    [Fact]
    public async Task Redis_lock_refresh_uses_a_token_checked_expiry_script_and_reports_a_lock_that_was_lost()
    {
        var mux = DispatchProxy.Create<IConnectionMultiplexer, CacheProviderDispatchProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        ((CacheProviderDispatchProxy)(object)mux).NextResult = database;
        var proxy = (RedisDatabaseProxy)(object)database;
        using var cacheLock = new RedisLock(mux, "tests");

        proxy.ScriptResult = 1L;
        (await cacheLock.RefreshAsync("order", "token", TimeSpan.FromSeconds(5))).Should().BeTrue();
        proxy.LastScript.Should().Contain("redis.call('get', KEYS[1]) == ARGV[1]").And.Contain("pexpire");

        proxy.ScriptResult = 0L;
        (await cacheLock.RefreshAsync("order", "token", TimeSpan.FromSeconds(5))).Should().BeFalse();
    }

    [Fact]
    public void The_redis_lock_has_both_lock_capabilities()
    {
        typeof(RedisLock).Should().Implement<Naravel.Cache.Abstractions.IRefreshableCacheLock>();
        typeof(RedisLock).Should().Implement<Naravel.Cache.Abstractions.IForceReleasableCacheLock>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Redis_lock_refresh_rejects_a_lease_of_zero_or_less_before_any_call(int seconds)
    {
        var mux = DispatchProxy.Create<IConnectionMultiplexer, CacheProviderDispatchProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        ((CacheProviderDispatchProxy)(object)mux).NextResult = database;
        var proxy = (RedisDatabaseProxy)(object)database;
        using var cacheLock = new RedisLock(mux, "tests");

        Func<Task> act = () => cacheLock.RefreshAsync("order", "token", TimeSpan.FromSeconds(seconds));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        proxy.LastScript.Should().BeNull("an invalid lease is refused before the server is asked");
    }

    [Fact]
    public async Task Redis_ttl_increment_uses_atomic_increment_and_expiry_script()
    {
        var mux = DispatchProxy.Create<IConnectionMultiplexer, CacheProviderDispatchProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        ((CacheProviderDispatchProxy)(object)mux).NextResult = database;
        var proxy = (RedisDatabaseProxy)(object)database;
        proxy.ScriptResult = 12L;
        using var store = new RedisCacheStore("tests", mux, "tests");

        (await store.IncrementAsync("window", 1, TimeSpan.FromSeconds(10))).Should().Be(12);

        proxy.LastScript.Should().Contain("redis.call('INCRBY'");
        proxy.LastScript.Should().Contain("redis.call('PTTL'");
        proxy.LastScript.Should().Contain("redis.call('PEXPIRE'");
    }

    private static (RedisCacheStore Store, RedisFlushServerProxy Server, RedisFlushDatabaseProxy Database) FlushFixture()
    {
        var multiplexer = DispatchProxy.Create<IConnectionMultiplexer, RedisFlushMultiplexerProxy>();
        var database = DispatchProxy.Create<IDatabase, RedisFlushDatabaseProxy>();
        var server = DispatchProxy.Create<IServer, RedisFlushServerProxy>();
        var muxProxy = (RedisFlushMultiplexerProxy)(object)multiplexer;
        muxProxy.Database = database;
        muxProxy.Server = server;
        return (new RedisCacheStore("one", multiplexer, "app[one]"), (RedisFlushServerProxy)(object)server, (RedisFlushDatabaseProxy)(object)database);
    }

    [Fact]
    public async Task Redis_prefix_flush_scans_the_escaped_prefix_inside_the_store_prefix_and_deletes_only_what_matches()
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.ExpectedPattern = "app\\[one\\]:tenant\\*:*";
        server.MatchPrefix = "app[one]:tenant*:";
        server.Keys = ["app[one]:tenant*:a", "app[one]:tenant*:b", "app[one]:tenantX:c", "app[one]:other"];

        await store.FlushPrefixAsync("tenant*:");

        server.LastPattern.Should().Be("app\\[one\\]:tenant\\*:*");
        database.DeletedKeys.Should().Equal("app[one]:tenant*:a", "app[one]:tenant*:b");
    }

    [Fact]
    public async Task Redis_flush_never_deletes_the_keys_of_a_lock_that_shares_the_store_prefix()
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Keys = ["app[one]:owned", "app[one]:lock:order"];

        await store.FlushAsync();

        database.DeletedKeys.Should().Equal("app[one]:owned");

        database.DeletedKeys.Clear();
        server.ExpectedPattern = "app\\[one\\]:lock:*";
        server.MatchPrefix = "app[one]:lock:";

        await store.FlushPrefixAsync("lock:");

        database.DeletedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task The_key_that_RedisLock_writes_is_a_key_that_the_flush_skips()
    {
        var mux = DispatchProxy.Create<IConnectionMultiplexer, CacheProviderDispatchProxy>();
        var lockDatabase = DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();
        ((CacheProviderDispatchProxy)(object)mux).NextResult = lockDatabase;
        using var cacheLock = new RedisLock(mux, "app[one]");
        (await cacheLock.AcquireAsync("order", TimeSpan.FromSeconds(5))).Should().NotBeNull();
        var lockKey = ((RedisDatabaseProxy)(object)lockDatabase).LastKey!;
        lockKey.Should().Be("app[one]:" + RedisLock.KeyNamespace + "order");

        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Keys = [lockKey];
        await store.FlushAsync();

        database.DeletedKeys.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Redis_flush_fails_without_removing_anything_when_a_primary_is_not_connected(bool prefixFlush)
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Connected = false;
        server.Keys = ["app[one]:owned"];

        Func<Task> flush = async () => { if (prefixFlush) await store.FlushPrefixAsync("owned"); else await store.FlushAsync(); };

        var error = (await flush.Should().ThrowExactlyAsync<InvalidOperationException>()).Which;
        error.Message.Should().Contain("not connected").And.Contain("Nothing was removed");
        server.LastPattern.Should().BeNull("a primary that is down is reported before any scan starts");
        database.DeletedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Redis_flush_ignores_a_replica_even_when_it_is_not_connected()
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Replica = true;
        server.Connected = false;
        server.Keys = ["app[one]:owned"];

        await store.FlushAsync();

        server.LastPattern.Should().BeNull();
        database.DeletedKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Redis_flush_deletes_every_key_when_there_are_more_than_one_batch()
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Keys = Enumerable.Range(0, 600).Select(i => $"app[one]:key-{i}").ToArray();

        await store.FlushAsync();

        database.DeletedKeys.Should().BeEquivalentTo(server.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Redis_prefix_flush_rejects_an_empty_prefix_before_it_scans(string prefix)
    {
        var (store, server, database) = FlushFixture();
        using var _ = store;
        server.Keys = ["app[one]:owned"];

        Func<Task> flush = async () => await store.FlushPrefixAsync(prefix);

        await flush.Should().ThrowAsync<ArgumentException>();
        server.LastPattern.Should().BeNull();
        database.DeletedKeys.Should().BeEmpty();
    }

    public class RedisDatabaseProxy : DispatchProxy
    {
        public long ScriptResult { get; set; }
        public string? LastScript { get; private set; }
        public string? LastKey { get; private set; }
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.StringSetAsync))
            {
                LastKey = args?[0]?.ToString();
                return Task.FromResult(true);
            }

            if (targetMethod?.Name == nameof(IDatabase.ScriptEvaluateAsync))
            {
                LastScript = args?[0]?.ToString();
                return Task.FromResult(RedisResult.Create((RedisValue)ScriptResult));
            }
            throw new NotSupportedException($"Unexpected Redis call: {targetMethod?.Name}");
        }
    }

    public class RedisFlushMultiplexerProxy : DispatchProxy
    {
        public IDatabase? Database { get; set; }
        public IServer? Server { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "GetDatabase" => Database,
            "GetEndPoints" => new EndPoint[] { new DnsEndPoint("redis", 6379) },
            "GetServer" => Server,
            nameof(IDisposable.Dispose) => null,
            _ => throw new NotSupportedException($"Unexpected Redis multiplexer call: {targetMethod?.Name}")
        };
    }

    public class RedisFlushServerProxy : DispatchProxy
    {
        public string[] Keys { get; set; } = [];
        public string? LastPattern { get; private set; }
        public bool Connected { get; set; } = true;
        public bool Replica { get; set; }

        // The pattern the store must send, and the key prefix that the real server would match it to.
        public string ExpectedPattern { get; set; } = "app\\[one\\]:*";
        public string MatchPrefix { get; set; } = "app[one]:";

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_IsConnected") return Connected;
            if (targetMethod?.Name == "get_IsReplica") return Replica;
            if (targetMethod?.Name == nameof(IServer.KeysAsync))
            {
                LastPattern = args?[1]?.ToString();
                if (LastPattern != ExpectedPattern)
                {
                    throw new InvalidOperationException($"Unexpected Redis scan pattern: {LastPattern}");
                }

                return EnumerateKeys(Keys.Where(key => key.StartsWith(MatchPrefix, StringComparison.Ordinal)));
            }

            throw new NotSupportedException($"Unexpected Redis server call: {targetMethod?.Name}");
        }

        private static async IAsyncEnumerable<RedisKey> EnumerateKeys(
            IEnumerable<string> keys,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var key in keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return key;
                await Task.Yield();
            }
        }
    }

    public class RedisFlushDatabaseProxy : DispatchProxy
    {
        public List<string> DeletedKeys { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_Database") return 0;
            if (targetMethod?.Name == nameof(IDatabase.KeyDeleteAsync))
            {
                DeletedKeys.Add(args?[0]?.ToString() ?? string.Empty);
                return Task.FromResult(true);
            }

            throw new NotSupportedException($"Unexpected Redis database call: {targetMethod?.Name}");
        }
    }
}
