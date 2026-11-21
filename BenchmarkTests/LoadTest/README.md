# Load test: one published build against another

The HTTP/1.1 keep-alive load comparison used for the changelog numbers since 4.0.0 (the BenchmarkDotNet
`--endpoints` benchmarks are JIT only and measure single requests; this measures the published `npgsqlrest`
builds under load, JIT and native AOT).

Files:

- `setup.sql`: the database: a text value, a PL/pgSQL function with five queries, a POST with four parameters,
  100 rows, one 170 KB json value, and a text value and 100 rows with `user_context`.
- `LoadGen.cs.txt`, `loadgen.csproj.txt`: the load generator. They are kept as `.txt` so the solution does not
  build them; copy them to a directory as `Program.cs` and `loadgen.csproj` and `dotnet build -c Release -o out`.
  Per endpoint it warms up, then measures the response time on 1 connection, requests per second on 32, and CPU
  per request on 8 (the server's from `ps`, PostgreSQL's from the container's cgroup).
- `run.sh <rounds> <seconds>`: expects `builds/<variant>/` directories next to it (`jit…` started with
  `dotnet NpgsqlRestClient.dll`, `aot…` with `./NpgsqlRestClient`), the load generator in `loadgen/out/`, and
  the PostgreSQL container `npgsqlrest-benchmark-pg` on port 55432. Variants rotate by one position every round.
  Rows go to `results.tsv`: label, endpoint, response bytes, response time in µs, requests per second, server
  CPU in µs per request, PostgreSQL CPU in µs per request.

Before running: publish both versions the same way (the previous release from a `git worktree` of its tag), and
delete any `appsettings.Development.json` from the publish directories, or the builds run with different settings.
Read `Baselines/` for earlier results and the cautions that apply (order effects, connection reset settings).
