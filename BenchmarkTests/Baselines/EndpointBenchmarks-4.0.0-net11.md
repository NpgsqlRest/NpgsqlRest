# EndpointBenchmarks: .NET 10 (3.21.0) vs .NET 11 (4.0.0 upgrades)

Recorded 2026-11-11, on the same machine and PostgreSQL container as the baseline (`EndpointBenchmarks-3.21.0.md`): Apple M4 Pro, PostgreSQL 17.10 in `npgsqlrest-benchmark-pg`.

## Why the original baseline is not used directly

BenchmarkDotNet 0.15.8, the latest stable version and the one used for the baseline, cannot run on .NET 11. It fails with `NotImplementedException: GetRuntimeVersion not implemented for NotRecognized`. The project now uses **0.16.0-preview.2**.

Changing the measurement tool changes the numbers by itself. For example, the .NET 10 allocations read 7.19 KB under 0.15.8 and 7.13 KB under the preview. So the .NET 10 side was **measured again with the same preview tool**, as a control. The control used the unmodified 3.21.0 commit, with only the benchmark files added. Two of the four pairs alternated the .NET 11 and .NET 10 runs, so both saw the same machine conditions.

| | .NET 10 control | .NET 11 |
|---|---|---|
| Code | `6cc6f55` (3.21.0), net10.0, LangVersion 14 | branch `4.0.0` (all projects net11.0, packages upgraded) |
| Runtime | .NET 10.0.0 (10.0.25.52411) | .NET 11.0.0 (11.0.26.53018) |
| ASP.NET Core / Microsoft.Extensions | 10.0.x | 11.0.0 |
| Npgsql | 10.0.3 | 10.0.3 (no 11.x exists) |
| BenchmarkDotNet | 0.16.0-preview.2, DefaultJob | 0.16.0-preview.2, DefaultJob |

## Results (4 runs each)

Mean time per request (µs):

| Benchmark | .NET 10 runs | .NET 10 avg | .NET 11 runs | .NET 11 avg | Change |
|---|---|---:|---|---:|---:|
| GetText | 189.8, 181.6, 180.9, 179.5 | 183.0 | 191.4, 197.4, 191.1, 189.1 | 192.3 | **+9.3 µs (+5.1%)** |
| GetRows100 | 447.8, 438.2, 444.3, 439.1 | 442.4 | 457.5, 462.1, 456.5, 456.5 | 458.2 | **+15.8 µs (+3.6%)** |

Allocated per request (whole process, client and server). Allocations are deterministic, so these are identical in every run:

| Benchmark | .NET 10 | .NET 11 | Change |
|---|---:|---:|---:|
| GetText | 7.13 KB | 7.27 KB | **+0.14 KB (+2.0%)** |
| GetRows100 | 190.67 KB | 190.87 KB | +0.20 KB (+0.1%) |

## Reading

- On .NET 11, each request takes about 10–16 µs longer, roughly 4–5%. Every .NET 11 run is slower than every .NET 10 run for GetRows100. For GetText, every .NET 11 run except the 197.4 µs outlier lies between 189.1 and 191.4 µs, while three of the four .NET 10 runs are at or below 181.6 µs. So this is a small but consistent regression, not noise.
- Each request also allocates about 150–200 bytes more, and the amount is the same for both benchmarks. That points to a fixed per-request cost in the HTTP stack (HttpClient, Kestrel, hosting or runtime), not to row serialization.
- This benchmark sends one request at a time, with the client and server in the same process and the Concurrent Workstation GC. It measures fixed latency per request, not throughput under load.

## Where the time goes (layered run)

Two reference benchmarks were added to separate the platform's share from NpgsqlRest's:

- `HttpOnly`: the same HTTP round trip to a plain `MapGet` endpoint that writes `Hello, World!`, with no NpgsqlRest and no database.
- `NpgsqlOnly`: the same database call made directly through Npgsql (open a pooled connection, execute, read the scalar), with no HTTP.

These are 2 alternating pairs, with the same tool and the same machine as above:

| Benchmark | .NET 10 runs (µs) | .NET 11 runs (µs) | Time change (avg) | Allocated change |
|---|---|---|---:|---:|
| HttpOnly | 33.99, 34.49 | 41.93, 37.70 | **+5.6 µs (+16%)** | 2.99 → 3.03 KB (~+40 B) |
| NpgsqlOnly | 131.59, 137.02 | 138.42, 131.13 | +0.5 µs (noise) | 1.55 → 1.57 KB (~+20 B) |
| GetText | 184.32, 181.81 | 198.09, 191.18 | +11.6 µs (+6.3%) | 7.13 → 7.28 KB (~+160 B) |
| GetRows100 | 460.50, 446.92 | 476.80, 453.62 | +11.5 µs (+2.5%, noisy) | 190.68 → 190.86 KB |

- **About half of the extra time is in .NET 11 itself.** A plain ASP.NET Core endpoint with no NpgsqlRest code is about 5.6 µs slower on .NET 11 (HttpClient, Kestrel and routing on loopback, macOS arm64), and both .NET 11 runs are slower than both .NET 10 runs.
- **Npgsql shows no regression:** the times are within noise.
- **About 5–6 µs and roughly 100 B per request are left over,** and they appear only on NpgsqlRest's own request path. This part is ours to investigate. Either NpgsqlRest code compiled against .NET 11 binds to different APIs (or C# 15 generates different code), or some API it uses behaves differently on the .NET 11 runtime. The layers don't add up exactly, because NpgsqlRest's database call isn't identical to `NpgsqlOnly`, so treat this split as approximate.

## Experiment: Runtime Async (`<Features>runtime-async=on</Features>`)

Runtime Async is the main .NET 11 performance feature that needs an opt-in, and it's still a preview feature for user code. The test build had it enabled for every project (NpgsqlRest core, plugins, client, benchmark). In that build, `NpgsqlRest.dll` has 7 compiler-generated async state machine types instead of 46, and the file shrinks from 678 KB to 592 KB (−13%). Both builds target .NET 11 and ran in 2 alternating pairs:

| Benchmark | off (µs) | on (µs) | Allocated off → on |
|---|---|---|---|
| GetText | 190.55, 185.38 | 190.80, 192.26 | **7.27 → 14.25 KB (×2)** |
| GetRows100 | 458.52, 461.46 | 444.83, 454.54 | **190.86 → 197.87 KB (+7 KB)** |
| HttpOnly | 41.10, 40.82 | 40.99, 38.96 | 3.03 → 2.80 KB |
| NpgsqlOnly | 139.31, 134.34 | 135.16, 134.43 | 1.57 → 1.55 KB |

**Verdict: don't enable it yet.** Time doesn't change meaningfully (GetRows100 is about 2% faster, within noise), but NpgsqlRest's own request path allocates about 7 KB more per request. The benchmark's small methods improve slightly (HttpOnly), so the extra allocation comes from NpgsqlRest's code. The likely cause is the large request-handler method: under runtime async, every suspension can allocate a continuation that holds its live local variables. Microsoft's performance post says there are "known cases where it still produces less efficient code". Revisit with .NET 12, where it's expected to become the default, or after splitting the handler into smaller async methods.

**Outcome:** it was enabled for every project for a short time, then **reverted**:
- Builds and the 2540 tests (which run under the JIT) were green, and the AOT publish succeeded.
- But the Native AOT client binary **crashed on the first request** (SIGBUS, exit 138, nothing logged). A clean control showed AOT with Runtime Async off works, AOT with it on crashes, and JIT with it on works. Small synthetic Runtime Async apps run fine under AOT, so the trigger is specific to NpgsqlRest's code.
- The AOT binary also grew from 36.09 MB to 38.78 MB (+7.5%), even though the managed assemblies shrank.

### Why the allocations grew: measured

An allocation-by-type probe ran the NpgsqlRest `GetText` request in-process 30,000 times, reading the runtime's GC allocation events:
- **Off:** 7.4 KB per request. The request handler's state machine box is 840 B, allocated once and reused.
- **On:** 19.4 KB per request. About 11.2 KB of it is runtime continuation objects of **13,704 B** each (some 7,832 B), plus about 53 `RuntimeAsyncTask` objects (72 B each) and about 16 `Action` delegates per request.

Under Runtime Async the JIT decides which locals to save at each suspension. In one method this large (`InvokeAsync`: 373 locals, 68 of them address-taken, 103 awaits) it saves about 1,700 slots. The classic C# compiler hoisted only the ~100 locals that are live across awaits.

Synthetic methods with up to 500 locals, address-taken locals and awaits in `finally` do *not* show this: they allocate the same or less than the classic model. Neither does awaiting classic-compiled code (about +30 B per suspension). So the cause is this one very large method, not Runtime Async in general.

A related finding: under the JIT (`DOTNET_JitDisasmSummary=1`), the classic `<InvokeAsync>d__7:MoveNext` (54 KB of IL) ends as **"Tier-0 switched MinOpts"**. The request handler has never been optimized in JIT mode. Splitting it into smaller methods is the prerequisite for both optimized JIT code and a Runtime Async benefit.

## Raw .NET 11 report (run 1)

```
BenchmarkDotNet v0.16.0-preview.2, macOS 27.0.1 (26A434) [Darwin 27.0.0]
Apple M4 Pro, 1 CPU, 14 logical and 14 physical cores
.NET SDK 11.0.100
  [Host]     : .NET 11.0.0 (11.0.0, 11.0.26.53018), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 11.0.0 (11.0.0, 11.0.26.53018), Arm64 RyuJIT armv8.0-a

| Method     | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------- |---------:|--------:|--------:|------:|--------:|--------:|-------:|----------:|------------:|
| GetText    | 191.4 us | 3.39 us | 3.76 us |  1.00 |    0.00 |  0.7324 |      - |   7.27 KB |        1.00 |
| GetRows100 | 457.5 us | 5.76 us | 5.38 us |  2.39 |    0.05 | 23.4375 | 3.4180 | 190.86 KB |       26.27 |
```
