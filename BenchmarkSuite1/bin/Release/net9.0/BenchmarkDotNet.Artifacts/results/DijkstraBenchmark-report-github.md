```

BenchmarkDotNet v0.15.2, Windows 11 (10.0.22631.6060/23H2/2023Update/SunValley3)
13th Gen Intel Core i7-1355U 1.70GHz, 1 CPU, 12 logical and 10 physical cores
.NET SDK 9.0.306
  [Host]     : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.10 (9.0.1025.47515), X64 RyuJIT AVX2


```
| Method                | Mean       | Error     | StdDev     | Gen0   | Gen1   | Allocated |
|---------------------- |-----------:|----------:|-----------:|-------:|-------:|----------:|
| &#39;Small Path (5,5)&#39;    |   5.375 μs | 0.2091 μs |  0.5863 μs | 6.5346 |      - |  40.13 KB |
| &#39;Medium Path (25,25)&#39; |  68.160 μs | 2.2213 μs |  6.0054 μs | 6.9580 | 0.2441 |  42.85 KB |
| &#39;Long Path (49,49)&#39;   | 241.613 μs | 6.3326 μs | 17.7574 μs | 6.8359 |      - |  43.38 KB |
