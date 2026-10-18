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
| Warm `Naravel.Events` dispatch with one no-op listener | 137.8 ns | 24 B |

These are local baselines, not service-level throughput claims. BenchmarkDotNet reported a multimodal
distribution for source-generated serialization; rerun on the target hardware before comparing small
changes. Events was measured with a short three-iteration job on Ubuntu 24.04.5 / .NET 10.0.12; its
99.9% confidence interval was wide, so the number is an initial baseline only. The zero-allocation target
was not achieved: the dispatch context is allocated to support propagation control.

Run the Events benchmark with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*EventDispatchBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

Run the Database benchmark with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*DatabaseBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`DatabaseBenchmarks` (PDR-022 S2) compares building and compiling a query with a constant string (`CompileBuilder` against the `CompileFloor` baseline, no I/O), and running the same query on in-memory SQLite through the builder (`BuilderRows`, `BuilderTyped`), hand-written Dapper (`HandWrittenDapper`) and raw ADO.NET (`HandWrittenAdo`); each opens one connection per call. **No numbers are recorded yet:** the benchmark has not been run (the authoring environment has no .NET SDK). Record the first baseline here after running it on the CI or a developer machine, and treat a builder overhead that is large next to the SQLite round trip as a defect to fix before R20.
