using System.Reflection;
using Enyim.Caching;
using Enyim.Caching.Memcached.Results;
using Naravel.Cache.Abstractions;
using Naravel.Cache.Memcached;

namespace Naravel.Cache.Tests;

public sealed class MemcachedProviderTests
{
    [Fact]
    public async Task Cache_store_uses_typed_client_values_and_prefixes_keys()
    {
        var client = DispatchProxy.Create<IMemcachedClient, MemcachedClientProxy>();
        var proxy = (MemcachedClientProxy)(object)client;
        proxy.ReadValue = 42;
        proxy.HasValue = true;
        var store = new MemcachedCacheStore("memcached", client, "app:tenant");

        await store.SetAsync("answer", 42, TimeSpan.FromMinutes(3));
        var (found, value) = await store.TryGetAsync<int>("answer");

        found.Should().BeTrue();
        value.Should().Be(42);
        proxy.LastKey.Should().Be("app:tenant:answer");
        proxy.LastExpiration.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task Null_ttl_is_passed_as_memcached_no_expiration()
    {
        var client = DispatchProxy.Create<IMemcachedClient, MemcachedClientProxy>();
        var proxy = (MemcachedClientProxy)(object)client;
        var store = new MemcachedCacheStore("memcached", client, "app");

        await store.SetAsync("forever", "value", null);

        proxy.LastExpiration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Memcached_ttl_increment_creates_the_bucket_with_expiration()
    {
        var client = DispatchProxy.Create<IMemcachedClient, MemcachedClientProxy>();
        var proxy = (MemcachedClientProxy)(object)client;
        var store = new MemcachedCacheStore("memcached", client, "app");

        (await store.IncrementAsync("rate", 1, TimeSpan.FromSeconds(30))).Should().Be(1);

        proxy.LastAddKey.Should().Be("app:rate");
        proxy.LastAddExpiration.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Memcached_lock_checks_token_before_removing_key()
    {
        var client = DispatchProxy.Create<IMemcachedClient, CacheProviderDispatchProxy>();
        var proxy = (CacheProviderDispatchProxy)(object)client;
        proxy.NextResult = true;
        var cacheLock = new MemcachedLock(client, "tests");

        (await cacheLock.AcquireAsync("job", TimeSpan.FromSeconds(5))).Should().NotBeNull();
        proxy.NextResult = "owner-token";
        (await cacheLock.ReleaseAsync("job", "not-owner")).Should().BeFalse();
    }

    public class MemcachedClientProxy : DispatchProxy
    {
        public object? ReadValue { get; set; }
        public bool HasValue { get; set; }
        public string? LastKey { get; private set; }
        public TimeSpan? LastExpiration { get; private set; }
        public string? LastAddKey { get; private set; }
        public TimeSpan? LastAddExpiration { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "SetAsync")
            {
                LastKey = args?[0]?.ToString();
                LastExpiration = args is { Length: > 2 } && args[2] is TimeSpan expiration ? expiration : null;
                return Task.FromResult(true);
            }

            if (targetMethod?.Name == "GetAsync" && targetMethod.ReturnType.IsGenericType)
            {
                LastKey = args?[0]?.ToString();
                var resultContract = targetMethod.ReturnType.GetGenericArguments()[0];
                var valueType = resultContract.GetGenericArguments()[0];
                var factory = typeof(MemcachedClientProxy).GetMethod(nameof(CreateResult), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(valueType);
                var result = factory.Invoke(null, new[] { ReadValue, HasValue });
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultContract).Invoke(null, new[] { result });
            }

            if (targetMethod?.Name == "AddAsync")
            {
                LastAddKey = args?[0]?.ToString();
                LastAddExpiration = args is { Length: > 2 } && args[2] is TimeSpan expiration ? expiration : null;
                return Task.FromResult(true);
            }
            if (targetMethod?.Name == "GetValueAsync") return Task.FromResult(ReadValue);
            if (targetMethod?.Name == "RemoveAsync") return Task.FromResult(true);
            if (targetMethod?.Name == "FlushAllAsync") return Task.CompletedTask;
            throw new NotSupportedException($"Unexpected Memcached call: {targetMethod?.Name}");
        }

        private static IGetOperationResult<T> CreateResult<T>(object? value, bool hasValue)
        {
            var result = DispatchProxy.Create<IGetOperationResult<T>, GetResultProxy<T>>();
            var proxy = (GetResultProxy<T>)(object)result;
            proxy.ResultValue = value is T typed ? typed : default;
            proxy.ResultHasValue = hasValue;
            return result;
        }
    }

    public class GetResultProxy<T> : DispatchProxy
    {
        public T? ResultValue { get; set; }
        public bool ResultHasValue { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            "get_HasValue" => ResultHasValue,
            "get_Success" => ResultHasValue,
            "get_Value" => ResultValue,
            _ => targetMethod?.ReturnType.IsValueType == true ? Activator.CreateInstance(targetMethod.ReturnType) : null
        };
    }
}
