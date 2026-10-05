```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i7-7700 CPU 3.60GHz (Max: 2.30GHz) (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method             | Mean       | Error    | StdDev   | Gen0   | Allocated |
|------------------- |-----------:|---------:|---------:|-------:|----------:|
| MemoryQueuePushPop | 1,434.4 ns | 26.55 ns | 42.87 ns | 0.2670 |    1120 B |
| MemoryCacheGetSet  |   750.7 ns | 13.74 ns | 12.18 ns | 0.1640 |     688 B |
| TaggedCacheGet     |   236.3 ns |  3.81 ns |  4.07 ns | 0.1030 |     432 B |
