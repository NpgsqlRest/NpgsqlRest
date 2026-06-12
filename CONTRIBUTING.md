# Contributing to NpgsqlRest

Thank you for considering a contribution! Bug reports, documentation fixes, tests, and features are all welcome.

## Quick orientation

| Part | Path | What it is |
|---|---|---|
| Core library | `NpgsqlRest/` | The middleware: PostgreSQL routines/SQL files → REST endpoints (NuGet) |
| Client app | `NpgsqlRestClient/` | The shipped binary: config-driven host (auth, caching, static files, …) |
| Plugins | `plugins/` | TsClient, SqlFileSource, CrudSource, OpenApi, HttpFiles, Mcp (independent NuGet versions) |
| Tests | `NpgsqlRestTests/` | Integration tests against a real PostgreSQL |
| Docs | [npgsqlrest-docs](https://github.com/NpgsqlRest/npgsqlrest-docs) | Documentation website (separate repo) |

## Building and testing

Prerequisites: **.NET 10 SDK** and a local **PostgreSQL** (any recent version; CI tests 15/16/17) listening on `localhost:5432` with user `postgres` / password `postgres`. The test run creates and drops its own database (`npgsql_rest_test`); connection constants live in `NpgsqlRestTests/Setup/Database.cs`.

```sh
dotnet build
dotnet test NpgsqlRestTests/NpgsqlRestTests.csproj
```

## Test conventions (please follow these — PRs that don't will be asked to change)

- **Assert full response strings**, not fragments. A test pins the exact wire output.
- **SQL setup lives in the same file as the assertions**: add a `public static void YourTests()` method on the `Database` partial class appending your `create function …` to the shared script (it is auto-registered via reflection), with the `[Collection("TestFixture")]` test class below it. Copy the pattern from any file in `NpgsqlRestTests/BodyTests/`.
- Pitfalls: don't name test routines after PostgreSQL built-ins (`to_date`, …); `NameSimilarTo` treats `_` as a wildcard; response JSON keys are camelCase.
- Tests must be deterministic — no fixed sleeps; await observable conditions with timeouts.

## Code constraints

- **AOT/trim-safe only.** The client publishes with `PublishAot=true` + `TrimMode=full`: no reflection-based serialization or libraries, JSON via source-generated/`System.Text.Json.Nodes` patterns. (Note: `JsonArray.Add(x)` needs an explicit `(JsonNode?)` cast.)
- Match the surrounding code's style and comment density. Comments explain constraints, not narrate lines.
- Public API of the core library is a published NuGet surface — breaking changes need a strong justification and a changelog entry under "Breaking Changes".

## Pull requests

1. Open an issue first for anything non-trivial — agreeing on the approach saves everyone time.
2. Include tests for behavior changes (see conventions above).
3. Add a changelog entry to `changelog/v<next-version>.md` describing the change in user-facing terms.
4. Keep PRs focused — one logical change per PR.
5. CI must be green (build + full test suite on PostgreSQL 15/16/17).

## Reporting bugs

Use the bug-report issue template. The single most useful thing you can provide is a **minimal reproduction**: the SQL (`create function …` + comment annotations or `.sql` file), the relevant config section, the request, and the expected vs. actual full response.

## Security issues

**Do not open public issues for vulnerabilities** — see [SECURITY.md](SECURITY.md).

## Labels

- `good-first-issue` — small, well-scoped, a good entry point
- `help-wanted` — larger items looking for a contributor
- `roadmap` — planned by the maintainer
