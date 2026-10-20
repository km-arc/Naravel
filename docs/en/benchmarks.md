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

Run the Streaming benchmark with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*StreamingBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`StreamingBenchmarks` (PDR-022 R22.T04) visits every row of a 50 000 row in-memory SQLite table: `BufferedGet` (baseline) loads the whole result, `CursorEnumerate` streams over one open reader, `LazyPages` pages with `limit`/`offset`, and `LazyByIdPages` and `ChunkByIdPages` page by keyset on `id`. Read the `Allocated` column first: streaming exists to keep memory bounded, so the streamed variants should allocate per row visited, not per row held, while `BufferedGet` holds all 50 000 rows at once; and keyset paging should not slow down on deep pages the way `limit`/`offset` does. **No numbers are recorded yet:** the benchmark has not been run (the authoring environment has no .NET SDK). Record the first baseline here after running it on the CI or a developer machine; if a streamed variant is slower than `BufferedGet` by more than the per-row cost of the open reader, treat it as a defect.

Run the Writes benchmark with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*WritesBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`WritesBenchmarks` (PDR-022 R23.T06) writes 1 000 rows into an empty in-memory SQLite table: `RowByRowInserts` (baseline) runs one `InsertAsync` per row, `BatchInsert` sends the rows as one `InsertAsync` batch (one statement, one transaction) and `BatchUpsert` as one `UpsertAsync` batch. The batch should beat the loop clearly, because the loop pays a command and a commit per row, and the upsert should stay close to the plain batch. **No numbers are recorded yet:** the benchmark has not been run (the authoring environment has no .NET SDK). Record the first baseline here after running it on the CI or a developer machine; a batch that is slower than the loop is a defect.

Run the Pagination benchmark with:

```sh
dotnet run -c Release --project benchmarks/Naravel.Benchmarks -- --filter '*PaginationBenchmarks*' --job short --warmupCount 1 --iterationCount 3 --launchCount 1
```

`PaginationBenchmarks` (PDR-020 S2) reads one page of 20 rows from a 50 000 row in-memory SQLite table with `OFFSET` (`OffsetFirstPage`, `SimpleOffsetFirstPage`, `SimpleOffsetDeepPage`, `OffsetDeepPage`) and with a cursor (`KeysetFirstPage`, `KeysetDeepPage`); the deep page is page 2 000, and `SimpleOffsetDeepPage` is the baseline. The first-page rows are the control: the two styles should be about equal there, and the keyset deep page should stay close to its first page while the offset deep page grows with the offset. **No numbers are recorded yet:** the benchmark has not been run (the authoring environment has no .NET SDK). Record the first baseline here after running it on the CI or a developer machine. SQLite is the best case for `OFFSET` (no network, one process), so a gap here understates what a remote database shows.
