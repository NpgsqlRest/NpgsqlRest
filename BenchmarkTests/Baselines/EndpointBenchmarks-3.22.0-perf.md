# EndpointBenchmarks: 3.22.0 performance work

Recorded 2026-11-19 on the same machine and PostgreSQL container as the earlier baselines: Apple M4 Pro, PostgreSQL 17.10 in `npgsqlrest-benchmark-pg` (port 55432), .NET 11.0.0, BenchmarkDotNet 0.16.0-preview.2 DefaultJob. Run with:

```
NPGSQLREST_BENCHMARK_CONNECTION="Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet run -c Release -- --endpoints
```

The starting point is branch `3.22.0` with the security fixes applied, before any performance change. Each row adds one change to the previous row and is a single full run.

This document has three parts; the numbers quoted in the changelog are those of **round 3** (3.22.0 against the released 3.21.0 on .NET 10). **Round 1** (the sections up to "How the JIT verdict was checked") is the first set of changes, committed as `9d63171`. **Round 2** continues from there to the final request pipeline and compares it with the start of the performance work (`5befe76`, on .NET 11). **Round 3** compares the release with 3.21.0 on .NET 10, which also uncovered what resetting connections costs. Two statements in round 1 turned out to be wrong and are corrected in round 2: that methods with a pooling async builder stay classic when Runtime Async is on, and the ApacheBench numbers for the native AOT build.

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
- **Runtime Async** with the split handler: the native AOT build no longer crashes (it did with the monolithic handler) and its cost is ordinary, but it allocates about 0.65 KB per request more than the pooled classic build at equal latency. It stays off; see `Directory.Build.props`. (Round 2 explains where those bytes came from and removes most of them.)

## Native AOT comparison (superseded by round 2)

**These numbers understate the difference and are kept only as a record.** ApacheBench speaks HTTP/1.0. NpgsqlRest responses have no `Content-Length` (they are chunked for HTTP/1.1), so Kestrel answers an HTTP/1.0 request with `Connection: close`, and `ab -k` reports `Keep-Alive requests: 0`: every request opened a new TCP connection, and connection setup dominated what was measured. Round 2 uses an HTTP/1.1 keep-alive client.

BenchmarkDotNet runs the benchmark project as a JIT process, so all numbers above are JIT numbers. The native AOT `npgsqlrest` executable was measured separately: the AOT binaries of `5befe76` (before the performance work) and `9d63171` (after) were published for osx-arm64. Each was run against a fresh database on the same PostgreSQL container, with the two benchmark functions and default client configuration, and loaded with ApacheBench (`ab -k`) after a warm-up. Runs were interleaved.

Sequential (one request at a time, mean ms per request; two rounds each):

| | Text (20,000 requests) | 100 rows (5,000 requests) | RSS after load |
|---|---|---|---|
| before | 0.248, 0.245 | 0.523, 0.538 | 101, 105 MB |
| after | 0.241, 0.242 | 0.506, 0.519 | 98, 88 MB |

Concurrent (requests per second; three more rounds each):

| | 100 rows, c=8 | 100 rows, c=32 | Text, c=32 |
|---|---|---|---|
| before | 8,770 / 8,563 / 8,435 | 11,635 / 11,881 / 11,775 | 24,142 / 23,960 / 23,216 |
| after | 9,008 / 8,394 / 8,418 | 12,589 / 11,942 / 11,829 | 23,314 / 20,856 / 23,624 |

Reading: on AOT, sequential latency improves by 2–3.5%. Native AOT compiles every method with optimizations ahead of time; the JIT's MinOpts fallback for very large methods applies only at run time. So the AOT build never had the unoptimized-handler problem, and gains only from the smaller allocations. Concurrent throughput shows no change beyond noise; the single-threaded `ab` is likely the limit at these rates. The load scripts are not part of the repository; the setup above is enough to repeat the comparison.

## How the JIT verdict was checked

Run the JIT build of the client with `DOTNET_JitStdOutFile=jit.txt DOTNET_JitDisasmSummary=1`, send a few hundred requests, and look for the `NpgsqlRestEndpoint+<...>d__N:MoveNext` lines:

| | IL size | Final tier | Code size |
|---|---:|---|---:|
| `InvokeAsync` before the split | 55,098 | Tier-0 switched MinOpts | 151,960 |
| `InvokeCoreAsync` after the split | 20,600 | Tier1 with Synthesized PGO | 44,596 |
| `RenderResultAsync` | 11,106 | Tier1 with Synthesized PGO | 25,364 |
| `BindQueryStringParametersAsync` | 9,486 | Tier1 with Synthesized PGO | 17,036 |

---

# Round 2: UTF-8 rendering, request state, Runtime Async

Recorded 2026-11-19, same machine and container. "Before" is `5befe76` (security fixes, no performance work), as in round 1. "After" is the final 3.22.0 working tree. Every comparison was interleaved (before, after, before, after ...).

## What changed after round 1

| Change | Effect (JIT allocation profile, bytes per request incl. the test client) |
|---|---|
| Rows rendered as UTF-8 bytes straight from the reader (`RowRenderer`, `PgUtf8Converters`, `Utf8Buffer`) instead of one string per value | 100 rows: 119,620 → 53,843 |
| Rows sent once 16 KB is collected (`ResponseBufferBytes`) instead of every 25 rows | about a tenth of the CPU of a 100-row request |
| A single value read as UTF-8 into a pooled buffer instead of a string | 119 KB json value: 607,621 → 366,731 (of which 358,024 is the client's own string and buffer) |
| One pooled `RequestState`, stages as small methods, one render method per result shape | text: 5,729 → 5,344; 100 rows: 53,153 → 52,738 |
| Endpoint calls the handler directly (no generated route wrapper), first execution attempt made by the request (no retry frame), scalar split into "execute" and "read + write" | text: 5,344 → 4,961; 100 rows: 52,738 → 52,391 |
| Row renderer reused through the request state; multi-statement SQL files use it too | one 144-byte object less per row request (not profiled separately) |

## JIT: BenchmarkDotNet

Two interleaved runs each. Mean time and allocation per request (client and server together).

| | GetText | GetRows100 | HttpOnly | NpgsqlOnly |
|---|---|---|---|---|
| before, run 1 | 182.65 µs, 7.29 KB | 438.76 µs, 190.87 KB | 39.28 µs, 3.03 KB | 131.49 µs, 1.57 KB |
| before, run 2 | 182.45 µs, 7.27 KB | 438.81 µs, 190.88 KB | 37.26 µs, 3.03 KB | 132.28 µs, 1.57 KB |
| after, run 1 | 179.12 µs, 4.90 KB | 402.72 µs, 50.91 KB | 38.25 µs, 3.03 KB | 131.22 µs, 1.57 KB |
| after, run 2 | 181.21 µs, 4.91 KB | 402.74 µs, 50.95 KB | 37.53 µs, 3.03 KB | 130.42 µs, 1.57 KB |
| after, Runtime Async ¹ | 178.43 µs, 4.94 KB | 397.63 µs, 51.06 KB | 39.64 µs, 2.80 KB | 131.77 µs, 1.53 KB |

GetRows100 collections per 1,000 requests: gen 0 23.44 → 6.35, gen 1 3.9 / 3.4 → 0.49.

¹ `NpgsqlRestRuntimeAsync=true dotnet run -c Release -- --endpoints`. It must be an environment variable: BenchmarkDotNet builds the benchmark in a child build, and a `-p:` property on the command line does not reach it. The benchmark project itself is then compiled with Runtime Async too, which is why the two controls allocate less (HttpOnly 3.03 → 2.80 KB); NpgsqlRest's own path allocates about 0.27 KB more, in line with the allocation profile below.

The benchmark client's own copy of the 15,217-character GetRows100 response is 44.6 KB (a 30,456-byte string and a 15,248-byte buffer), so everything else went from 146 KB to 6 KB. For GetText, HttpOnly + NpgsqlOnly is 4.60 KB; NpgsqlRest's addition went from 2.68 KB to 0.30 KB.

## Load test: JIT and native AOT, HTTP/1.1 keep-alive

The published builds (`dotnet NpgsqlRestClient.dll` for JIT, the osx-arm64 native executable for AOT), default configuration, a fresh database with three functions: `benchmark_get_text` (13 bytes), `benchmark_get_rows(100)` (15,217 bytes), `benchmark_get_json(1500)` (one 119,322-byte json value). The load client is a small .NET `HttpClient` program (HTTP/1.1, keep-alive, one connection per worker); it and PostgreSQL run on the same machine. Per variant: warm-up, then sequential latency (1 connection), throughput (32 connections), and server CPU per request at 8 connections (process CPU time from `ps` before and after, divided by the requests). Three rounds; values are round 1 / 2 / 3.

Server CPU per request, µs (8 connections):

| | Text | 100 rows | 119 KB json |
|---|---|---|---|
| JIT before | 159.5 / 158.0 / 161.9 | 458.9 / 511.1 / 501.0 | 880.8 / 901.7 / 910.0 |
| JIT after | 151.9 / 154.9 / 153.8 | 287.2 / 289.9 / 290.6 | 540.0 / 541.7 / 537.5 |
| JIT after, Runtime Async | 154.1 / 153.8 / 153.2 | 290.1 / 289.8 / 292.4 | 547.5 / 534.2 / 540.0 |
| AOT before | 168.9 / 170.6 / 167.4 | 545.4 / 567.6 / 552.5 | 823.3 / 891.7 / 836.7 |
| AOT after | 162.5 / 160.2 / 163.4 | 359.0 / 353.4 / 348.6 | 582.5 / 568.3 / 557.5 |
| AOT after, Runtime Async | 165.8 / 158.2 / 161.9 | 365.5 / 350.4 / 351.5 | 596.7 / 551.7 / 562.5 |

Requests per second (32 connections) and mean response time (1 connection, µs):

| | Text req/s | 100 rows req/s | Text µs | 100 rows µs | 119 KB json µs | RSS after load |
|---|---|---|---|---|---|---|
| JIT before | 34,820 / 34,924 / 35,227 | 13,940 / 13,559 / 14,055 | 193.0 / 193.8 / 197.9 | 459.8 / 448.6 / 451.9 | 2,580.9 / 2,605.4 / 2,545.9 | 243 / 236 / 239 MB |
| JIT after | 33,583 / 35,339 / 34,938 | 18,455 / 18,336 / 18,433 | 196.6 / 197.0 / 194.6 | 403.0 / 401.5 / 403.4 | 2,308.8 / 2,291.9 / 2,295.8 | 145 / 145 / 141 MB |
| JIT after, Runtime Async | 34,758 / 35,347 / 34,891 | 17,866 / 18,079 / 17,779 | 192.8 / 195.1 / 192.0 | 399.8 / 396.6 / 401.6 | 2,427.1 / 2,331.1 / 2,267.2 | 149 / 148 / 147 MB |
| AOT before | 34,353 / 34,417 / 34,587 | 12,474 / 13,070 / 12,625 | 196.2 / 198.0 / 198.2 | 454.4 / 456.0 / 449.0 | 2,140.8 / 2,519.5 / 2,523.7 | 136 / 133 / 140 MB |
| AOT after | 34,053 / 32,736 / 32,990 | 16,696 / 16,734 / 16,829 | 193.8 / 194.4 / 195.0 | 423.1 / 422.2 / 421.5 | 2,205.3 / 2,201.3 / 2,181.5 | 65 / 61 / 64 MB |
| AOT after, Runtime Async | 33,356 / 34,573 / 32,955 | 16,031 / 16,868 / 16,519 | 199.0 / 197.1 / 195.9 | 425.6 / 430.2 / 429.5 | 2,132.8 / 2,144.3 / 2,161.4 | 60 / 65 / 59 MB |

The variants ran in the order listed, and the first one in a round has a small advantage on the text endpoint. A second AOT run of the text endpoint alone, in reverse order (200,000 requests, 32 connections): before 34,220 / 33,277 / 33,129, after 33,556 / 33,698 / 33,396, Runtime Async 35,017 / 34,439 / 33,359. So text throughput is the same for all three.

Native AOT binary (osx-arm64): 36,172,976 bytes before, 36,189,504 after, 35,906,528 with Runtime Async. The same six trim/AOT warnings in all (ExcelDataReader, Microsoft.Extensions.Caching.Hybrid).

## Runtime Async: what it costs and why

Allocation profile (in-process server and client, one request at a time, `AllocationSampled` events, bytes per request):

| | Text, classic | Text, Runtime Async | 100 rows, classic | 100 rows, Runtime Async |
|---|---:|---:|---:|---:|
| After round 1 | 5,729 | 6,700 | 53,153 | 54,177 |
| Request state + stage methods | 5,344 | 5,851 | 52,738 | 53,091 |
| Final | 4,961 | 5,248 | 52,391 | 52,692 |

Three things were learned with small experiments (a console program measuring bytes per suspension for different method shapes):

- **The pooling attribute is ignored.** With `runtime-async=on`, the compiler does not use `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]`: those methods become Runtime Async methods like any other. (Round 1 assumed they stayed classic. A source-defined `RuntimeAsyncMethodGenerationAttribute(false)` does opt a method out.)
- **A method's continuation is sized for its largest suspension point.** A suspended Runtime Async method costs about 48 bytes plus 8 bytes per value that is live across the wait, liveness is precise, and the object is reused when the same method suspends again. But the layout is one per method, sized for the suspension point with the most live values. After round 1, `InvokeCoreAsync` and `RenderResultAsync` each allocated 448–480 bytes whenever a request waited for the database, because somewhere else in each method fifty-odd values were live across some other wait. Small methods with few locals cost 56–96 bytes.
- **Waiting on code that is not Runtime Async costs extra.** Awaiting a `Task` from classically compiled code (Npgsql) allocates a `RuntimeAsyncTaskContinuation` (72 bytes) and usually an `Action` (56 bytes); a Runtime Async method called through a delegate or by classic code needs a `RuntimeAsyncTask` (72 bytes).

What remains per request with Runtime Async: one `RuntimeAsyncTask`, two small continuations (56 + 96 bytes on the scalar path), and the boundary objects for the wait inside Npgsql: about 350 bytes. The classic build's equivalent is one 80-byte `ValueTaskSourceAsTask`, since its state machine boxes are pooled. That is the 0.3 KB difference, and it cannot be removed from this side: it goes away when Npgsql itself is compiled with Runtime Async.

Verification of the Runtime Async build: the full test suite (2,998 tests) passes with `-p:NpgsqlRestRuntimeAsync=true`, and the native AOT executable built with it passes `.github/scripts/aot-smoke-test.sh`.

---

# Round 3: 3.22.0 against 3.21.0 on .NET 10, Runtime Async, and connection resets

Recorded 2026-11-19, same machine and container. Rounds 1 and 2 compared commits of the 3.22.0 branch with each other. This round compares what ships with the previous release: **3.21.0** built from `master` (`6cc6f55`) with the .NET 10 SDK (10.0.100, runtime 10.0.0), **3.22.0** on .NET 11, and 3.22.0 on .NET 11 compiled with Runtime Async (`-p:NpgsqlRestRuntimeAsync=true`), each as a JIT build (`dotnet NpgsqlRestClient.dll`) and as a native AOT executable (osx-arm64). Published the standard way, default settings.

Load method as in round 2 (HTTP/1.1 keep-alive client, sequential latency at 1 connection, throughput at 32, server CPU per request at 8), with one more endpoint: `benchmark_plpgsql(_id int)`, a PL/pgSQL function that runs five small queries against a 1,000-row table. Three rounds per table, each in a different order of the variants; values are round 1 / 2 / 3.

## What this round found

Against its own starting commit the branch looked uniformly faster. Against 3.21.0 the small endpoints were 12-15% slower under load and the PL/pgSQL function 41% slower. The cause was not .NET 11 and not async: 3.21.0 forced `No Reset On Close=true`; the `user_context` security fix (`d23ad8c`) removed that, so Npgsql sent `DISCARD ALL` before every request - verified with `log_statement = 'all'` - and `DISCARD ALL` includes `DISCARD PLANS`, so PL/pgSQL statements were planned again on every request.

Two changes followed (see the changelog, "Connections are reset only when needed"): connections are reset only when an endpoint can leave session-level settings on them (`NpgsqlRestOptions.ConnectionResetRequired`), and when they are reset the client keeps a prepared statement on each physical connection so that Npgsql sends `SET SESSION AUTHORIZATION DEFAULT;RESET ALL;CLOSE ALL;UNLISTEN *;SELECT pg_advisory_unlock_all();DISCARD SEQUENCES;DISCARD TEMP` instead of `DISCARD ALL` (also verified in the statement log).

## Load test

### Final: default settings (no endpoint leaves session state, so connections are not reset)

Requests per second at 32 connections and mean response time at 1 connection (µs):

| | Text req/s | PL/pgSQL req/s | 100 rows req/s | Text µs | PL/pgSQL µs | 100 rows µs | 119 KB json µs |
|---|---|---|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 38,977 / 39,693 / 39,056 | 32,698 / 32,767 / 32,445 | 16,948 / 16,377 / 16,126 | 181.9 / 183.9 / 182.0 | 250.7 / 258.4 / 255.3 | 453.4 / 459.5 / 448.1 | 2444.5 / 2369.6 / 2398.0 |
| JIT 3.22.0 | 39,120 / 38,347 / 40,018 | 32,083 / 32,885 / 31,255 | 20,185 / 20,550 / 20,679 | 190.0 / 191.3 / 190.2 | 262.4 / 259.6 / 259.5 | 429.1 / 412.3 / 419.5 | 2226.2 / 2288.5 / 2256.1 |
| JIT 3.22.0 Runtime Async | 38,706 / 40,617 / 39,720 | 32,632 / 32,037 / 31,829 | 20,671 / 19,535 / 20,376 | 191.9 / 188.3 / 188.5 | 259.7 / 264.7 / 261.2 | 420.2 / 423.0 / 427.1 | 2195.8 / 2214.0 / 2249.2 |
| AOT 3.21.0 (.NET 10) | 39,744 / 39,283 / 38,186 | 31,402 / 31,894 / 32,246 | 15,220 / 15,694 / 15,479 | 183.2 / 181.3 / 183.7 | 251.9 / 250.9 / 249.5 | 470.5 / 483.9 / 488.6 | 2381.9 / 2383.0 / 2346.8 |
| AOT 3.22.0 | 37,238 / 38,385 / 38,786 | 31,493 / 29,976 / 31,053 | 18,413 / 18,705 / 19,003 | 191.7 / 192.8 / 190.1 | 258.7 / 261.0 / 260.9 | 470.0 / 466.2 / 481.2 | 2114.5 / 2134.3 / 2113.2 |
| AOT 3.22.0 Runtime Async | 37,919 / 37,431 / 38,158 | 29,876 / 32,320 / 30,892 | 18,656 / 19,110 / 19,075 | 191.6 / 191.7 / 188.3 | 261.0 / 257.4 / 263.2 | 470.3 / 477.2 / 477.9 | 2179.8 / 2093.8 / 2115.2 |

Server CPU per request at 8 connections (µs) and RSS after the load (MB):

| | Text | 100 rows | 119 KB json | RSS |
|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 198.1 / 203.5 / 202.4 | 421.2 / 403.0 / 451.2 | 835.0 / 910.0 / 901.7 | 327 / 333 / 338 |
| JIT 3.22.0 | 145.8 / 146.6 / 143.6 | 271.8 / 274.1 / 272.8 | 479.2 / 488.3 / 484.2 | 149 / 149 / 144 |
| JIT 3.22.0 Runtime Async | 146.5 / 143.0 / 134.6 | 277.5 / 272.9 / 267.9 | 492.5 / 473.3 / 462.5 | 149 / 145 / 148 |
| AOT 3.21.0 (.NET 10) | 158.5 / 158.8 / 157.2 | 461.8 / 447.5 / 460.2 | 782.5 / 780.0 / 777.5 | 246 / 250 / 247 |
| AOT 3.22.0 | 151.4 / 150.2 / 152.4 | 328.9 / 324.1 / 328.0 | 505.0 / 481.7 / 493.3 | 65 / 62 / 65 |
| AOT 3.22.0 Runtime Async | 151.5 / 147.4 / 150.5 | 329.5 / 323.9 / 316.6 | 483.3 / 497.5 / 485.0 | 67 / 65 / 66 |

### With the connection reset (the lighter one that keeps plans), as an application with user_context gets it

Requests per second at 32 connections and mean response time at 1 connection (µs):

| | Text req/s | PL/pgSQL req/s | 100 rows req/s | Text µs | PL/pgSQL µs | 100 rows µs | 119 KB json µs |
|---|---|---|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 38,736 / 38,985 / 39,614 | 32,606 / 32,273 / 32,351 | 16,826 / 16,352 / 16,695 | 182.0 / 185.8 / 182.7 | 257.8 / 252.9 / 257.4 | 454.8 / 445.9 / 456.4 | 2399.3 / 2519.6 / 2409.3 |
| JIT 3.22.0 | 35,068 / 34,839 / 34,518 | 27,736 / 27,394 / 26,542 | 18,050 / 18,247 / 18,429 | 197.4 / 199.2 / 199.0 | 277.1 / 277.0 / 275.5 | 418.0 / 414.7 / 417.0 | 2317.4 / 2287.2 / 2273.4 |
| JIT 3.22.0 Runtime Async | 35,456 / 35,047 / 34,766 | 27,321 / 27,450 / 27,162 | 18,308 / 18,058 / 17,904 | 197.3 / 196.3 / 197.6 | 278.1 / 279.8 / 276.8 | 415.0 / 417.2 / 413.5 | 2281.4 / 2334.4 / 2246.8 |
| AOT 3.21.0 (.NET 10) | 39,760 / 39,294 / 41,203 | 31,725 / 32,617 / 32,104 | 15,558 / 15,808 / 15,366 | 183.5 / 180.8 / 180.4 | 253.1 / 253.8 / 259.0 | 479.3 / 490.1 / 477.3 | 2407.7 / 2453.7 / 2464.2 |
| AOT 3.22.0 | 33,464 / 31,848 / 34,579 | 26,515 / 26,675 / 27,715 | 17,141 / 17,580 / 17,272 | 202.6 / 200.6 / 199.3 | 279.2 / 274.1 / 279.3 | 436.1 / 440.2 / 437.0 | 2188.6 / 2156.0 / 2142.5 |
| AOT 3.22.0 Runtime Async | 34,096 / 33,211 / 31,372 | 27,142 / 26,297 / 25,657 | 16,919 / 16,807 / 17,167 | 202.0 / 203.0 / 202.7 | 282.8 / 277.0 / 275.7 | 435.3 / 434.1 / 432.8 | 2151.9 / 2114.4 / 2167.4 |

Server CPU per request at 8 connections (µs) and RSS after the load (MB):

| | Text | 100 rows | 119 KB json | RSS |
|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 195.1 / 193.4 / 192.0 | 431.0 / 438.4 / 434.0 | 910.0 / 884.2 / 891.7 | 330 / 327 / 334 |
| JIT 3.22.0 | 153.1 / 153.8 / 148.8 | 301.9 / 294.8 / 287.8 | 566.7 / 566.7 / 560.8 | 143 / 142 / 148 |
| JIT 3.22.0 Runtime Async | 154.6 / 154.6 / 153.1 | 300.6 / 297.2 / 294.1 | 560.0 / 570.8 / 551.7 | 145 / 148 / 144 |
| AOT 3.21.0 (.NET 10) | 156.9 / 159.6 / 158.2 | 458.0 / 456.9 / 366.6 | 755.8 / 762.5 / 752.5 | 239 / 242 / 248 |
| AOT 3.22.0 | 166.8 / 157.5 / 159.8 | 366.5 / 344.4 / 353.9 | 595.0 / 557.5 / 570.0 | 61 / 65 / 60 |
| AOT 3.22.0 Runtime Async | 163.4 / 162.6 / 165.0 | 355.2 / 346.5 / 356.1 | 582.5 / 578.3 / 588.3 | 60 / 60 / 64 |

### .NET 10 against .NET 11 with the same 3.22.0 code (both with `DISCARD ALL`, before the reset rule)

Requests per second at 32 connections and mean response time at 1 connection (µs):

| | Text req/s | 100 rows req/s | Text µs | 100 rows µs | 119 KB json µs |
|---|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 39,908 / 39,701 / 39,338 | 16,597 / 16,504 / 16,956 | 188.7 / 181.1 / 183.7 | 441.7 / 446.7 / 446.1 | 2408.0 / 2433.7 / 2514.1 |
| JIT 3.22.0 code on .NET 10 | 34,767 / 34,084 / 34,264 | 18,557 / 18,437 / 18,535 | 189.7 / 189.9 / 187.2 | 392.8 / 390.7 / 391.5 | 2092.7 / 2114.1 / 2059.3 |
| JIT 3.22.0 (.NET 11) | 34,474 / 35,043 / 35,315 | 18,645 / 18,489 / 18,258 | 194.4 / 193.6 / 194.8 | 398.9 / 396.5 / 398.1 | 2323.7 / 2283.2 / 2320.2 |
| JIT 3.22.0 Runtime Async | 35,813 / 34,988 / 35,444 | 18,311 / 18,270 / 18,883 | 192.0 / 196.2 / 192.4 | 402.8 / 404.5 / 400.6 | 2289.6 / 2269.2 / 2213.2 |
| AOT 3.21.0 (.NET 10) | 41,238 / 39,076 / 38,328 | 14,805 / 15,352 / 15,363 | 182.0 / 181.8 / 180.9 | 458.9 / 466.8 / 464.6 | 2445.8 / 2605.1 / 2365.3 |
| AOT 3.22.0 code on .NET 10 | 35,966 / 36,284 / 33,788 | 17,088 / 17,403 / 17,433 | 192.0 / 193.1 / 193.1 | 424.7 / 418.9 / 422.5 | 2078.1 / 2062.4 / 2068.2 |
| AOT 3.22.0 (.NET 11) | 33,472 / 33,692 / 34,813 | 16,675 / 16,297 / 16,993 | 194.8 / 190.6 / 195.8 | 422.0 / 427.5 / 428.4 | 2150.9 / 2177.4 / 2172.9 |
| AOT 3.22.0 Runtime Async | 33,589 / 32,967 / 33,690 | 16,590 / 16,612 / 16,919 | 194.0 / 196.9 / 197.8 | 426.2 / 426.7 / 429.3 | 2194.1 / 2183.4 / 2150.3 |

Server CPU per request at 8 connections (µs) and RSS after the load (MB):

| | Text | 100 rows | 119 KB json | RSS |
|---|---|---|---|---|
| JIT 3.21.0 (.NET 10) | 192.4 / 194.5 / 202.2 | 417.6 / 428.2 / 384.2 | 807.5 / 871.7 / 880.0 | 335 / 341 / 345 |
| JIT 3.22.0 code on .NET 10 | 211.6 / 208.9 / 212.8 | 360.6 / 357.8 / 362.6 | 600.0 / 593.3 / 577.5 | 282 / 280 / 283 |
| JIT 3.22.0 (.NET 11) | 152.1 / 153.1 / 150.8 | 290.5 / 290.1 / 285.9 | 531.7 / 550.0 / 529.2 | 141 / 140 / 141 |
| JIT 3.22.0 Runtime Async | 153.4 / 155.0 / 150.6 | 292.1 / 292.2 / 285.0 | 556.7 / 551.7 / 530.0 | 144 / 143 / 148 |
| AOT 3.21.0 (.NET 10) | 156.5 / 158.5 / 158.6 | 442.9 / 435.8 / 405.1 | 769.2 / 745.0 / 764.2 | 245 / 244 / 248 |
| AOT 3.22.0 code on .NET 10 | 205.9 / 212.1 / 205.8 | 363.2 / 392.8 / 389.9 | 622.5 / 625.8 / 613.3 | 203 / 203 / 204 |
| AOT 3.22.0 (.NET 11) | 162.5 / 161.9 / 164.2 | 361.1 / 355.0 / 357.8 | 579.2 / 565.8 / 569.2 | 61 / 60 / 65 |
| AOT 3.22.0 Runtime Async | 159.6 / 160.0 / 163.4 | 355.4 / 354.8 / 355.5 | 569.2 / 563.3 / 564.2 | 60 / 61 / 66 |


The third table is where "what .NET 11 itself changes" in the changelog comes from. It was measured before the reset rule existed, so both 3.22.0 builds in it reset with `DISCARD ALL`, and 3.21.0 (which never reset) is listed only for orientation.

Runtime Async against standard async, final builds: every figure is within the spread between rounds. The 2-3% lower row throughput noted in round 2 was an order effect (Runtime Async always ran last there); with the order varied it is gone.

## What resetting connections costs

The same 3.22.0 binaries with the three reset behaviors. "No reset" is the final build with default settings (three rounds), "lighter reset" the build that always reset (three rounds), `DISCARD ALL` the builds before the client change (JIT: six runs for text, three for PL/pgSQL; AOT: five and two). Averages:

| | No reset | Lighter reset | `DISCARD ALL` | 3.21.0 (no reset) |
|---|---:|---:|---:|---:|
| JIT, text req/s (32 connections) | 39,162 | 34,808 | 34,940 | 39,242 |
| JIT, PL/pgSQL req/s | 32,074 | 27,224 | 23,223 | 32,637 |
| JIT, PL/pgSQL response time | 260.5 µs | 276.5 µs | 355.8 µs | 254.8 µs |
| JIT, text response time | 190.5 µs | 198.5 µs | 193.9 µs | 182.6 µs |
| AOT, text req/s (32 connections) | 38,136 | 33,297 | 33,928 | 39,071 |
| AOT, PL/pgSQL req/s | 30,841 | 26,968 | 21,904 | 31,847 |
| AOT, PL/pgSQL response time | 260.2 µs | 277.5 µs | 359.5 µs | 250.8 µs |
| AOT, text response time | 191.5 µs | 200.8 µs | 195.6 µs | 182.7 µs |

Reading: any reset costs 11-15% of the throughput of a small request, because PostgreSQL executes it on every request. For a trivial SQL function the lighter reset is no cheaper than `DISCARD ALL` (seven commands instead of one; slightly slower, in fact). What it saves is the re-planning: the five-statement PL/pgSQL function goes from 356-360 µs to 277 µs, against 260 µs without a reset.

One oddity for whoever repeats this: with one connection, the 100-row endpoint answers about 5% *faster* with a reset than without (the small reply to the reset arrives first and the read of the rows that follows is quicker). It does not show in throughput or CPU, and it is one reason to weight those more than single-connection response times.

## JIT: BenchmarkDotNet against master

The endpoint benchmark was copied into a worktree of `master` (it did not exist in 3.21.0) with the same BenchmarkDotNet version. Interleaved runs, mean time and allocation per request:

| | GetText | GetRows100 | HttpOnly | NpgsqlOnly |
|---|---|---|---|---|
| 3.21.0, .NET 10, run 1 | 177.19 µs, 7.13 KB | 425.20 µs, 190.68 KB | 30.47 µs, 2.99 KB | 125.16 µs, 1.55 KB |
| 3.22.0, .NET 11, run 1 | 180.18 µs, 4.91 KB | 394.10 µs, 51.02 KB | 38.88 µs, 3.03 KB | 131.70 µs, 1.57 KB |
| 3.21.0, .NET 10, run 2 | 176.77 µs, 7.13 KB | 425.02 µs, 190.68 KB | 30.22 µs, 2.99 KB | 129.12 µs, 1.55 KB |
| 3.22.0, .NET 11, run 2 | 180.98 µs, 4.90 KB | 400.76 µs, 50.97 KB | 39.36 µs, 3.03 KB | 131.76 µs, 1.57 KB |

GetRows100 collections per 1,000 requests: gen 0 23.44 -> 6.35, gen 1 3.42 -> 0.49. The Runtime Async figures are those of round 2 (the library did not change afterwards).

The two controls are the interesting part for .NET 11: the same plain HTTP round trip takes 30.3 µs on .NET 10 and 39.1 µs on .NET 11, the same Npgsql query 127.1 µs and 131.7 µs. Together that is 13.4 µs more on .NET 11 for work NpgsqlRest is not involved in, which is why GetText is 3.6 µs slower although NpgsqlRest's own part (GetText minus the two controls) went from 19.6 µs to 9.8 µs. The benchmark uses the library with a plain connection string, so both versions reset their connections with `DISCARD ALL`; the reset rule lives in the `npgsqlrest` client and plays no part here.

Native AOT binary (osx-arm64): 35,343,856 bytes for 3.21.0, 36,255,760 for 3.22.0, 35,972,768 with Runtime Async.

Verification of the final state: 3,013 tests pass on .NET 11 and on .NET 10 (`-p:NpgsqlRestTestFramework=net10.0`); both final AOT executables pass `.github/scripts/aot-smoke-test.sh`.

