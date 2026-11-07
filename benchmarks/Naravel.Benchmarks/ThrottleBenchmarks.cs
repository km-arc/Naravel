using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Naravel.Benchmarks;

/// <summary>
/// The route <c>throttle</c> middleware with the default in-process store and with the shared store over the Memory cache store
/// (R31.T05, PDR-024). The in-process row is the baseline: it must not be slower than it was before the <c>IThrottleStore</c> seam
/// (compare it with the same <see cref="ThrottleScenario"/> built on a commit from before R31.T01). The Memory row costs what the
/// shared store costs on top of that without a network: hashing the partition key, building the key and one atomic increment in the
/// cache. The call count (one store call per request) is proved by a test, not by this benchmark; the benchmark adds time and
/// allocations.
/// </summary>
[MemoryDiagnoser]
public class ThrottleBenchmarks
{
    private ThrottleScenario _inProcess = null!;
    private ThrottleScenario _sharedOverMemory = null!;

    [GlobalSetup]
    public void Setup()
    {
        _inProcess = new ThrottleScenario(configure: null);
        _sharedOverMemory = new ThrottleScenario(services =>
        {
            services.AddNaravelCache(new ConfigurationBuilder().Build());
            services.AddNaravelSharedThrottle("memory");
        });
    }

    [Benchmark(Baseline = true)]
    public Task InProcessStore() => _inProcess.InvokeAsync();

    [Benchmark]
    public Task SharedStoreOverMemory() => _sharedOverMemory.InvokeAsync();

    [GlobalCleanup]
    public void Cleanup()
    {
        _inProcess.Dispose();
        _sharedOverMemory.Dispose();
    }
}
