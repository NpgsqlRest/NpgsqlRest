# NpgsqlRest

[![Build, Test, Publish and Release](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml)
[![Tests](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/test.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/test.yml)
![1800+ integration tests](https://img.shields.io/badge/integration%20tests-1837%2B-brightgreen)
![License](https://img.shields.io/badge/license-MIT-green)
![GitHub Stars](https://img.shields.io/github/stars/NpgsqlRest/NpgsqlRest?style=social)
![GitHub Forks](https://img.shields.io/github/forks/NpgsqlRest/NpgsqlRest?style=social)

**Automatic REST API for PostgreSQL** | [6.1x faster than PostgREST](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2026.html)

> SQL files and PostgreSQL objects become REST endpoints. TypeScript clients are generated automatically.

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
| **SQL Files** | Simple queries, multi-command batch scripts, no DB deployment needed | `sql/get_users.sql` → `GET /api/get-users` |
| **Functions & Procedures** | Full PL/pgSQL power, static type checking, reusable logic | `get_user_by_id(int)` → `GET /api/get-user-by-id` |
| **Tables & Views** | Automatic CRUD | `users` table → `GET/POST/PUT/DELETE /api/users` |

SQL files are the easiest way to get started — drop a `.sql` file in a folder and you have an endpoint. Functions give you the full power of PL/pgSQL with true end-to-end type checking. Use both together, or whichever fits.

All sources share the same annotation system: `@authorize`, `@param`, `@returns`, `@void`, `@single`, `@cached`, `@path`, and 50+ others.

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

## Documentation

**[npgsqlrest.github.io](https://npgsqlrest.github.io/)** — getting started, configuration, annotations, examples.

## Contributing

Contributions are welcome. Open a pull request with a description of your changes.

## License

MIT
