# EndpointBenchmarks: 3.22.0 performance work

Recorded 2026-11-19 on the same machine and PostgreSQL container as the earlier baselines: Apple M4 Pro, PostgreSQL 17.10 in `npgsqlrest-benchmark-pg` (port 55432), .NET 11.0.0, BenchmarkDotNet 0.16.0-preview.2 DefaultJob. Run with:

```
NPGSQLREST_BENCHMARK_CONNECTION="Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet run -c Release -- --endpoints
```

The starting point is branch `3.22.0` with the security fixes applied, before any performance change. Each row adds one change to the previous row and is a single full run.

## Results

Mean time (µs) and allocations per request (whole process, client and server):

| Step | GetText µs | GetText alloc | GetRows100 µs | GetRows100 alloc | HttpOnly µs | NpgsqlOnly µs |
|---|---:|---:|---:|---:|---:|---:|
| Start (security fixes, no perf work) | 202.18 | 7.28 KB | 467.67 | 190.90 KB | 42.10 | 133.29 |
| StringBuilderPool off-by-one fixed | 198.71 | 6.71 KB | 450.83 | 117.46 KB | 40.25 | 139.89 |
| No `IServiceProvider` handler parameter (no DI scope) ¹ | 190.69 | 6.39 KB | 449.05 | 117.15 KB | 41.58 | 135.47 |
| `Response.BodyWriter` instead of `PipeWriter.Create(Response.Body)` | 185.85 | 6.04 KB | 449.13 | 116.72 KB | 40.42 | 133.78 |
| Split 1: rendering moved out of `InvokeAsync` (+ lazy retry lists) | 193.69 | 6.18 KB | 431.86 | 116.92 KB | 42.40 | 139.69 |
| Split 1 + pooled async builders on the new methods | 190.18 | 5.80 KB | 444.46 | 116.56 KB | 40.82 | 133.17 |
| Split 2: parameter binding moved out (binding-state object) | 188.21 | 5.93 KB | 419.50 | 116.67 KB | 41.16 | 134.34 |
| Pooled builder for the request method itself (`InvokeCoreAsync`) ¹ | 186.83 | 5.54 KB | 421.56 | 116.30 KB | 40.55 | 131.58 |
| **Final**: route handler as an RDG method group `(HttpContext, CancellationToken)` | **184.99** | **5.71 KB** | **419.77** | **116.54 KB** | 40.28 | 132.10 |
| Experiment, not shipped: Runtime Async on, pooled builders removed ² | 184.11 | 6.36 KB | 416.90 | 117.13 KB | 38.28 | 133.56 |

¹ These rows mapped the handler through a `Func<HttpContext, Task>` variable. That falls back to ASP.NET's reflection-based delegate binding: AOT warnings IL3050/IL2026 and a 2.6 MB larger native binary. The final row is a method group that the Request Delegate Generator binds at compile time. Its generated `async` wrapper costs about 0.17 KB more per request whenever the request waits, but it is AOT-safe and keeps endpoint filters working.

² Runtime Async applies to the whole build, so the HttpOnly control (client-side code) changed as well.

## Reading

- **Allocations** are deterministic and the most reliable signal: GetText 7.28 → 5.71 KB (−22%), GetRows100 190.9 → 116.5 KB (−39%). The StringBuilderPool fix accounts for nearly all of the row-endpoint saving.
- **Times** were noisy on this day: several other PostgreSQL containers were running, and the controls varied by ±5% (NpgsqlOnly 131.6–139.9 µs). Comparisons are therefore made against the controls. GetRows100 relative to NpgsqlOnly went from 3.36–3.51 before the split to 3.12–3.18 after it. That is the handler being JIT-optimized: before the split, `InvokeAsync`'s state machine (55 KB IL, over 2,000 basic blocks) was always compiled as "Tier-0 switched MinOpts"; afterwards every part reaches "Tier1 with Synthesized PGO". GetText relative to NpgsqlOnly stayed at about 1.39–1.40, since its time is dominated by the HTTP and database round trips.
- **Runtime Async** with the split handler: the native AOT build no longer crashes (it did with the monolithic handler) and its cost is ordinary, but it allocates about 0.65 KB per request more than the pooled classic build at equal latency. It stays off; see `Directory.Build.props`.

## How the JIT verdict was checked

Run the JIT build of the client with `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1`, send a few hundred requests, and look for the `NpgsqlRestEndpoint+<...>d__N:MoveNext` lines:

| | IL size | Final tier | Code size |
|---|---:|---|---:|
| `InvokeAsync` before the split | 55,098 | Tier-0 switched MinOpts | 151,960 |
| `InvokeCoreAsync` after the split | 20,600 | Tier1 with Synthesized PGO | 44,596 |
| `RenderResultAsync` | 11,106 | Tier1 with Synthesized PGO | 25,364 |
| `BindQueryStringParametersAsync` | 9,486 | Tier1 with Synthesized PGO | 17,036 |
