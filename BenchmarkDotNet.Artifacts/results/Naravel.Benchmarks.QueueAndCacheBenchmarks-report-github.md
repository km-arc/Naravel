```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i7-7700 CPU 3.60GHz (Max: 4.00GHz) (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                   | Mean     | Error     | StdDev    | Gen0   | Allocated |
|------------------------- |---------:|----------:|----------:|-------:|----------:|
| MemoryRateLimiterAttempt | 5.803 μs | 12.977 μs | 0.7113 μs | 0.9918 |   4.07 KB |
