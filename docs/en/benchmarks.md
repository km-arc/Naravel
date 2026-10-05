# Benchmark Baselines

Benchmarks use BenchmarkDotNet 0.15.8 on Ubuntu 24.04.4, .NET SDK 10.0.112, .NET runtime 10.0.12,
Intel Core i7-7700 (one CPU available). Run them with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*'
```

| Benchmark | Mean | Allocated |
|---|---:|---:|
| Memory queue push + pop + ack | 1.434 us | 1,120 B |
| Memory cache set + get | 750.7 ns | 688 B |
| Tagged cache get | 236.3 ns | 432 B |
| Reflection JSON serialize + deserialize | 481.9 ns | 208 B |
| Source-generated JSON serialize + deserialize | 301.4 ns | 88 B |

These are local baselines, not service-level throughput claims. BenchmarkDotNet reported a multimodal
distribution for source-generated serialization; rerun on the target hardware before comparing small
changes.