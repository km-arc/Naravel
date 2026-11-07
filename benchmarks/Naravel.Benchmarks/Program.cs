using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Naravel.Benchmarks;

// The Redis throttle benchmark needs a server (NARAVEL_BENCH_REDIS). Without one it is left out and every other benchmark runs as before.
// The classes are chosen here, with BenchmarkSwitcher.FromTypes, because the documented options of the BenchmarkDotNet console only
// select benchmarks (--filter, --anyCategories, --allCategories, --attribute) and none of them leaves a class out.
var redisConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ThrottleRedisBenchmarks.ConnectionVariable));
if (!redisConfigured)
{
    Console.WriteLine($"{ThrottleRedisBenchmarks.ConnectionVariable} is not set: {nameof(ThrottleRedisBenchmarks)} is left out.");
}

// Every concrete class of this assembly that has a [Benchmark] method, which is what the classes of this project are. A class added
// later is picked up without an edit here.
var benchmarkTypes = typeof(Program).Assembly
    .GetTypes()
    .Where(static type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false } &&
        type.GetMethods().Any(static method => method.IsDefined(typeof(BenchmarkAttribute), inherit: false)))
    .Where(type => redisConfigured || type != typeof(ThrottleRedisBenchmarks))
    .ToArray();

BenchmarkSwitcher.FromTypes(benchmarkTypes).Run(args);
