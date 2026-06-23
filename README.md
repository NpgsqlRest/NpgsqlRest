# NpgsqlRest

[![Build, Test, Publish and Release](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml)
[![Tests](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/test.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/test.yml)
![1800+ integration tests](https://img.shields.io/badge/integration%20tests-1837%2B-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)
![GitHub Stars](https://img.shields.io/github/stars/NpgsqlRest/NpgsqlRest?style=social)
![GitHub Forks](https://img.shields.io/github/forks/NpgsqlRest/NpgsqlRest?style=social)

# Your SQL is the API.

**Automatic REST API for PostgreSQL** | [6.1x faster than PostgREST](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2026.html)

> SQL files and PostgreSQL objects become REST endpoints. TypeScript clients are generated automatically.

**4,500+ req/s** on a single host · **1,800+ integration tests** · **12K LOC SQL** in production · **MIT** licensed

*"Simplicity is the ultimate sophistication."* — Leonardo da Vinci

SQL is declarative — your API should be too. With NpgsqlRest, you write SQL and annotate it with comments to declare what you want from your endpoint: caching, timeouts, retries, authorization, rate limiting, and everything in between. No controllers, no models, no mapping layers. Backend features become simple declarations on your SQL objects, putting PostgreSQL at the dead center of your architecture — the opposite of Clean Architecture, which treats the database as a detail. Here, PostgreSQL *is* the architecture.

<p align="center">
  <img src="clean.png?v=2" alt="NpgsqlRest Architecture" width="340">
</p>

## Install

| Method | Command |
|--------|---------|
| **NPM** | `npm i npgsqlrest` |
| **Docker** | `docker pull vbilopav/npgsqlrest:latest` |
| **Direct Download** | [Releases](https://github.com/NpgsqlRest/NpgsqlRest/releases) |
| **.NET Library** | `dotnet add package NpgsqlRest` |

Requires PostgreSQL >= 13. Native executables have zero runtime dependencies.

## From SQL to REST API

Write a SQL file:

```sql
-- sql/process_order.sql
-- HTTP POST
-- @authorize admin
-- @param $1 order_id
-- @result validate
select count(*) as found from orders where id = $1;
update orders set status = 'processing' where id = $1;
-- @result confirm
select id, status from orders where id = $1;
```

That gives you `POST /api/process-order`:

```json
{"validate": [1], "result2": 1, "confirm": [{"id": 42, "status": "processing"}]}
```

And a generated TypeScript client with full type safety:

```typescript
export async function processOrder(orderid: number) : Promise<{
    validate: number[],
    result2: number,
    confirm: { id: number, status: string }[]
}> {
    const response = await fetch(baseUrl + "/api/process-order", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ orderid }),
    });
    return await response.json();
}
```

No framework, no ORM, no boilerplate. Authorization, parameters, type safety — from a SQL file.

## Endpoint Sources

| Source | What it's good for | Example |
|--------|-----------|---------|
| **SQL Files** *(recommended starting point)* | Simple queries, multi-command batch scripts, no DB deployment needed | `sql/get_users.sql` → `GET /api/get-users` |
| **Functions & Procedures** | Full PL/pgSQL power, static type checking, reusable logic | `get_user_by_id(int)` → `GET /api/get-user-by-id` |

SQL files are the easiest way to get started — drop a `.sql` file in a folder and you have an endpoint. Functions give you the full power of PL/pgSQL with true end-to-end type checking. Use both together, or whichever fits.

All sources share the same annotation system: `@authorize`, `@param`, `@returns`, `@void`, `@single`, `@cached`, `@path`, and 50+ others.

## Declarative Annotations

Declare what you want from your endpoint — caching, authorization, timeouts, retries, rate limiting, output format — right where the SQL lives:

```sql
/*
HTTP GET /users/
@authorize admin, user
@cached
@cache_expires_in 30sec
@timeout 5min
@table_format = excel
@excel_file_name = users.xlsx
*/
select id, name, email, role
from users
where $1 is null or department_id = $1;
```

Same pattern for PostgreSQL functions via `comment on function ... is '...'`. No middleware to register, no decorators, no controllers — the SQL is the source of truth. See [all annotations](https://npgsqlrest.github.io/annotations/).

## Features

- **Multi-command SQL scripts** — multiple statements in one file execute as a batch, returning named result sets
- **TypeScript/JS code generation** and `.http` files — types flow from PostgreSQL to your frontend
- **AOT-compiled native binaries** — zero dependencies, instant startup
- [**6.1x faster than PostgREST**](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2026.html) at 100 concurrent users
- **50+ comment annotations** — `@authorize`, `@param`, `@returns`, `@void`, `@single`, `@result`, `@skip`, `@cached`, `@proxy`, and more
- **Auth** — cookie auth, Basic auth, JWT claims, role-based access, `@authorize`, `@allow_anonymous`
- **Column-level encryption**, security-sensitive endpoints, IP address binding
- **Response caching** with per-endpoint expiration control
- **Rate limiting** per endpoint
- **SSE streaming** via `RAISE INFO/NOTICE` with graceful shutdown
- **File uploads** — large objects, file system, MIME filtering
- **Reverse proxy** — forward to upstream services, transform proxy responses
- **HTTP custom types** — PostgreSQL composite types that call external APIs and return structured responses
- **Composite type support** — nested JSON, arrays of composites, `@returns` to skip Describe
- **OpenAPI 3.0** spec generation
- **CSV/Excel/HTML** table format response handlers

### How does it compare?

**[NpgsqlRest vs PostgREST vs Supabase](https://npgsqlrest.github.io/blog/npgsqlrest-vs-postgrest-supabase-comparison.html)**

## From the Blog

- [**Case Study: 74 Endpoints, Zero Backend Code**](https://npgsqlrest.github.io/blog/case-study-zero-backend-code) — A production app built entirely on NpgsqlRest: ~74 endpoints, 12K LOC of SQL, zero C# or Python.
- [**PostgreSQL REST API Benchmark 2026**](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2026) — 14 frameworks, identical PostgreSQL functions: NpgsqlRest vs PostgREST, Django, FastAPI, Spring Boot, Go, Rust, and more.
- [**From SQL to Type-Safe TypeScript**](https://npgsqlrest.github.io/blog/typescript-codegen-walkthrough) — End-to-end type safety: typed fetch modules generated directly from PostgreSQL functions and SQL files.

## Documentation

**[npgsqlrest.github.io](https://npgsqlrest.github.io/)** — getting started, configuration, annotations, examples.

## Claude Code skill

A [Claude Code](https://claude.com/claude-code) skill that teaches the agent how to build with NpgsqlRest — both endpoint sources (database functions/procedures/tables/views and `.sql` files), the full annotation set, configuration, HTTP custom types, proxy, caching, auth, SSE, and MCP. It lives in [`.claude/skills/npgsqlrest/`](.claude/skills/npgsqlrest/) and bundles a complete annotation reference and a full annotated `appsettings.json`.

Install it for your own NpgsqlRest projects — per-user (available everywhere):

```bash
mkdir -p "$HOME/.claude/skills/npgsqlrest"
for f in SKILL.md annotations-reference.md configuration-reference.jsonc; do
  curl -fsSL "https://raw.githubusercontent.com/NpgsqlRest/NpgsqlRest/master/.claude/skills/npgsqlrest/$f" \
    -o "$HOME/.claude/skills/npgsqlrest/$f"
done
```

…or scope it to a single project by copying those three files into `<your-project>/.claude/skills/npgsqlrest/`. The agent then loads it automatically when you work on NpgsqlRest SQL, annotations, or config. The two reference files are generated from `npgsqlrest --annotations` and `npgsqlrest --config`; regenerate them with those commands if your installed version differs.

## About

NpgsqlRest is built and maintained by [Vedran Bilopavlović](https://www.linkedin.com/in/vb-software/). The C# library, parser, codegen, and runtime are hand-written, covered by 1,800+ integration tests, and battle-tested in production.

## Contributing

Contributions are welcome — see **[CONTRIBUTING.md](CONTRIBUTING.md)** for how to build, run the tests, and what a good PR looks like. Small, well-scoped issues are labeled `good-first-issue`.

## Security

Please report vulnerabilities privately via [GitHub Private Vulnerability Reporting](https://github.com/NpgsqlRest/NpgsqlRest/security/advisories/new) — see **[SECURITY.md](SECURITY.md)**. Do not open public issues for security problems.

## License

MIT
