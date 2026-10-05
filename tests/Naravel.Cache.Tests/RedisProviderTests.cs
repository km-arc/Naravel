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

    public class RedisDatabaseProxy : DispatchProxy
    {
        public long ScriptResult { get; set; }
        public string? LastScript { get; private set; }
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IDatabase.StringSetAsync)) return Task.FromResult(true);
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

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "get_IsConnected") return true;
            if (targetMethod?.Name == "get_IsReplica") return false;
            if (targetMethod?.Name == nameof(IServer.KeysAsync))
            {
                LastPattern = args?[1]?.ToString();
                if (LastPattern != "app\\[one\\]:*")
                {
                    throw new InvalidOperationException($"Unexpected Redis scan pattern: {LastPattern}");
                }

                return EnumerateKeys(Keys.Where(key => key.StartsWith("app[one]:", StringComparison.Ordinal)));
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
