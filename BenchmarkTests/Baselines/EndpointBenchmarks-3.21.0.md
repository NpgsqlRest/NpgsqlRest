# EndpointBenchmarks baseline: 3.21.0 (before the 4.0.0 upgrades)

> **Superseded for the .NET 11 comparison:** these numbers were measured with BenchmarkDotNet 0.15.8, which cannot run on .NET 11. The before/after comparison, with the .NET 10 side measured again under BenchmarkDotNet 0.16.0-preview.2, is in `EndpointBenchmarks-4.0.0-net11.md`.

Recorded 2026-11-11 on branch `4.0.0` at `6cc6f55`, which has the same code as the 3.21.0 release. Taken **before** any 4.0.0 dependency or runtime upgrades, for a before/after comparison.

## What was measured

Full HTTP round trips: `HttpClient` → Kestrel (127.0.0.1) → NpgsqlRest → PostgreSQL → response. Requests run one at a time over a keep-alive connection. "Allocated" counts the whole process (client and server).

- `GetText`: `GET /api/benchmark-get-text` returns the scalar `Hello, World!`. This is the fixed cost of one request.
- `GetRows100`: `GET /api/benchmark-get-rows?count=100` returns 100 rows of int, text, bool, numeric, timestamptz, text[] and jsonb as a JSON array.

## Environment

| | |
|---|---|
| Machine | Apple M4 Pro, 14 cores, macOS 27.0.1 |
| Runtime | .NET 10.0.0 (Arm64 RyuJIT), Concurrent Workstation GC; Microsoft.AspNetCore.App 10.0.0 |
| SDK | .NET SDK 11.0.100 (global.json: 10.0.0 + rollForward latestMajor) |
| BenchmarkDotNet | 0.15.8, DefaultJob |
| Npgsql | 10.0.3 |
| PostgreSQL | 17.10, Docker `postgres:17-alpine` (image `sha256:742f40ea20b9…`), container `npgsqlrest-benchmark-pg` on port 55432 |

## Results

Run 1:

| Method     | Mean     | Error   | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------- |---------:|--------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| GetText    | 188.6 us | 3.73 us |  3.49 us |  1.00 |    0.03 |  0.4883 |      - |   7.19 KB |        1.00 |
| GetRows100 | 458.8 us | 8.97 us | 11.34 us |  2.43 |    0.07 | 23.4375 | 2.9297 | 190.73 KB |       26.53 |

Run 2 (repeated to see how much results vary between runs):

| Method     | Mean     | Error   | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|----------- |---------:|--------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| GetText    | 189.7 us | 3.56 us |  3.81 us |  1.00 |    0.03 |  0.4883 |      - |   7.19 KB |        1.00 |
| GetRows100 | 464.1 us | 9.25 us | 12.66 us |  2.45 |    0.08 | 23.4375 | 1.9531 | 190.72 KB |       26.52 |

The two runs differ by about 1% in mean time, and allocations are identical. After an upgrade, treat a time change of less than about 3% as noise. Any change in Allocated is real.

## Reproduce / compare after upgrading

Use the same machine and the same PostgreSQL container. Close other heavy apps first.

```bash
# PostgreSQL: reuse the same container (create it only if it no longer exists)
docker start npgsqlrest-benchmark-pg \
  || docker run -d --name npgsqlrest-benchmark-pg -e POSTGRES_PASSWORD=postgres -p 55432:5432 postgres:17-alpine

cd BenchmarkTests
NPGSQLREST_BENCHMARK_CONNECTION="Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=postgres" \
  dotnet run -c Release -- --endpoints

# Report: BenchmarkTests/BenchmarkDotNet.Artifacts/results/BenchmarkTests.EndpointBenchmarks-report-github.md
docker stop npgsqlrest-benchmark-pg
```

If the upgrade changes the target framework (for example to net11.0), the runtime line in the report changes too. That difference is part of what is being measured.
