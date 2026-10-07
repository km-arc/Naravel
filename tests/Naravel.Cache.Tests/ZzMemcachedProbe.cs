using Enyim.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Naravel.Cache.Memcached;
using Naravel.Testing;

namespace Naravel.Cache.Tests;

// TEMPORARY diagnostic probe; removed before merge.
public sealed class ZzMemcachedProbe
{
    [ServiceFact("NARAVEL_TEST_MEMCACHED")]
    public async Task Probe()
    {
        var endpoint = Environment.GetEnvironmentVariable("NARAVEL_TEST_MEMCACHED")!;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cache:Default"] = "memcached",
            ["Cache:Stores:memcached:Driver"] = "memcached",
            ["Cache:Stores:memcached:Servers"] = endpoint,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNaravelCache(configuration).AddNaravelMemcachedCache(configuration);
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IMemcachedClient>();

        var k = "probe:" + Guid.NewGuid().ToString("N");
        await client.SetAsync(k, "naravel", TimeSpan.FromMinutes(1));
        var o = await client.GetAsync<object>(k);
        var s = await client.GetAsync<string>(k);
        var v = await client.GetValueAsync<object>(k);
        var ng = await client.GetAsync(k);
        var rm = await client.RemoveAsync(k);

        var c = "probe:" + Guid.NewGuid().ToString("N");
        var added = await client.AddAsync(c, "1", TimeSpan.FromSeconds(30));
        var inc = client.Increment(c, 1UL, 1UL);
        var cs = await client.GetAsync<string>(c);
        var co = await client.GetAsync<object>(c);

        var n = "probe:" + Guid.NewGuid().ToString("N");
        await client.SetAsync(n, 41L, TimeSpan.FromMinutes(1));
        var no = await client.GetAsync<object>(n);
        var nl = await client.GetAsync<long>(n);

        throw new InvalidOperationException(
            $"PROBE obj={o.Success}/{o.HasValue}/{o.Value} str={s.Success}/{s.HasValue}/{s.Value} valObj={v} nongen={ng.Success}/{ng.HasValue}/{ng.Value} rm={rm} | " +
            $"ctr added={added} inc={inc} str={cs.Success}/{cs.HasValue}/{cs.Value} obj={co.Success}/{co.HasValue}/{co.Value} | " +
            $"long obj={no.Success}/{no.HasValue}/{no.Value} long={nl.Success}/{nl.HasValue}/{nl.Value}");
    }
}
