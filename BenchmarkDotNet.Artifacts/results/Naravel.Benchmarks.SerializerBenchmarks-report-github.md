```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Core i7-7700 CPU 3.60GHz (Max: 2.30GHz) (Kaby Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 10.0.112
  [Host]     : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3


```
| Method                   | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| ReflectionRoundTrip      | 481.9 ns |  8.26 ns |  7.33 ns |  1.00 |    0.02 | 0.0496 |     208 B |        1.00 |
| SourceGeneratedRoundTrip | 301.4 ns | 11.36 ns | 32.79 ns |  0.63 |    0.07 | 0.0210 |      88 B |        0.42 |
