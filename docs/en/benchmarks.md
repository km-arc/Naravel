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

## CI runner baselines (2026-10-28)

Measured by the report-only `evidence.yml` workflow (run 40758969223, `--job short`, 1 warmup and 3 iterations, one launch) on a GitHub-hosted `ubuntu-latest` runner: AMD EPYC 9V45, 4 logical cores, .NET SDK 10.0.403, runtime 10.0.14. Shared runners are noisy: two runs of the same code differed by up to 40% in time (the database benchmarks ran 29.9 us in one run and 42.9 us in another), so read the **allocations** (deterministic) and the **ratios** as the baseline and the times as an order of magnitude. Re-run with the workflow, or with the commands above, before comparing a change.

| Benchmark | Mean | Allocated | Note |
|---|---:|---:|---|
| Query builder compile (`CompileBuilder`) | 0.95-1.8 us | 3,776 B | `CompileFloor` is a constant string, so its ratio column is meaningless |
| Builder rows / typed on SQLite | 29.5-42.9 us | 9,552 / 7,880 B | about 5-6% over hand-written Dapper |
| Hand-written Dapper / ADO.NET | 26.1-38.9 us | 3,976 / 2,976 B | the floor the builder is compared with |
| Offset deep page / simple offset deep page | 446 / 374 us | 22.35 / 16.55 KB | keyset deep page 112 us, about 3.3x faster, 20 KB |
| Offset first page / keyset first page | 87 / 111 us | 22.2 / 16.35 KB | first pages are noise-level; the deep page is the point |
| Streaming: buffered `Get` / cursor | 62.8 / 49.7 ms | 15.81 / 14.81 MB | cursor 0.79x time, 0.94x allocation (50 000 rows) |
| Writes: row by row / batch insert | 54.3 / 21.8 ms | 4.34 MB / 0.97 MB | batch 0.41x time, 0.22x allocation (1 000 rows) |
| Memory queue push + pop + ack | 1.57 us | 1,144 B | |
| Memory cache set + get | 720 ns | 688 B | |
| Tagged cache get | 242 ns | 512 B | |
| Memory rate limiter attempt | 6.1 us | 4,168 B | includes the in-process lock |
| Failed job store record + list + forget | 8.2 us | 3,510 B | |
| Reflection / source-generated JSON round trip | 413 / 205 ns | 208 / 88 B | source generation 0.5x time, 0.42x allocation |
| Route middleware pipeline | 371 ns | 1.23 KB | |
| Warm `Naravel.Events` dispatch | 127 ns | 24 B | |

The older local baseline table above stays as the first reference; the allocation column agrees with it (queue 1,120 vs 1,144 B, cache 688 B, JSON 208 / 88 B, events 24 B).
