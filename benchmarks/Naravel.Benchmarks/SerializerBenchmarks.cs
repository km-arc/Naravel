using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using Naravel.Queue.Jobs;
using Naravel.Queue.Serialization;

namespace Naravel.Benchmarks;

[MemoryDiagnoser]
public class SerializerBenchmarks
{
    private readonly BenchmarkJob _job = new() { OrderId = 1001, Customer = "bench@example.com" };
    private JsonJobSerializer _reflection = null!;
    private JsonJobSerializer _sourceGenerated = null!;
    private string _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _reflection = new JsonJobSerializer(new BenchmarkJobRegistry());
        _sourceGenerated = new JsonJobSerializer(new BenchmarkJobRegistry(BenchmarkJsonContext.Default.BenchmarkJob));
        _payload = _reflection.Serialize(_job);
    }

    [Benchmark(Baseline = true)]
    public object ReflectionRoundTrip() => _reflection.Deserialize(_reflection.Serialize(_job), typeof(BenchmarkJob));

    [Benchmark]
    public object SourceGeneratedRoundTrip() => _sourceGenerated.Deserialize(_payload, typeof(BenchmarkJob));

    private sealed class BenchmarkJobRegistry(JsonTypeInfo? typeInfo = null) : IJobTypeRegistry
    {
        public void Register(Type type, string? alias = null, JsonTypeInfo? jsonTypeInfo = null) { }
        public string GetAlias(Type type) => "benchmark.job";
        public Type Resolve(string typeName) => typeof(BenchmarkJob);
        public JsonTypeInfo? GetJsonTypeInfo(Type type) => type == typeof(BenchmarkJob) ? typeInfo : null;
    }
}

public sealed class BenchmarkJob : IJob
{
    public int OrderId { get; set; }
    public string Customer { get; set; } = string.Empty;

    public Task HandleAsync(JobContext context, CancellationToken cancellationToken) => Task.CompletedTask;
}

[JsonSerializable(typeof(BenchmarkJob))]
internal partial class BenchmarkJsonContext : JsonSerializerContext;