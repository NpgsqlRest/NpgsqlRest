# NpgsqlRest

[![Build, Test, Publish and Release](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml)
![License](https://img.shields.io/badge/license-MIT-green)
![GitHub Stars](https://img.shields.io/github/stars/NpgsqlRest/NpgsqlRest?style=social)
![GitHub Forks](https://img.shields.io/github/forks/NpgsqlRest/NpgsqlRest?style=social)

**Automatic REST API Server for PostgreSQL**

> Transform your PostgreSQL database into a production-ready REST API server with automatic TypeScript code generation and end-to-end type safety.

<p align="center">
  <img src="clean.png" alt="NpgsqlRest Architecture" width="500">
</p>

**[Documentation](https://npgsqlrest.github.io/)** | **[Getting Started](https://npgsqlrest.github.io/getting-started/)** | **[Configuration Reference](https://npgsqlrest.github.io/config-reference/)** | **[Annotation Guide](https://npgsqlrest.github.io/comment-annotations/)**

## Key Features

- **Instant API Generation** - Automatically creates REST endpoints from PostgreSQL functions, procedures, tables, and views
- **Declarative Configuration** - Configure endpoints directly in your database using comment annotations
- **Code Generation** - Auto-generate frontend TypeScript/JavaScript code and .http files for testing
- **High Performance** - check out [benchmarks](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2026.html) showing low latency and high throughput. Latest benchmark show that it is 6.1x faster than PostgREST at 100 concurrent users, 
- **Native Executables** - AOT-compiled binaries with zero dependencies and instant startup
- **RESTful Path Parameters** - Define routes like `/products/{id}` with parameters extracted from URL paths
- **Reverse Proxy Endpoints** - Forward requests to upstream services with passthrough or transform modes
- **HTTP Custom Types** - Enable PostgreSQL functions to call external APIs via annotated composite types
- **Enterprise Ready** - Authentication, authorization, rate limiting, caching, SSE streaming, OpenAPI 3.0, and more

## Clean Architecture Done Right

Clean Architecture tells us to keep business logic independent of frameworks, databases, and delivery mechanisms. In practice, this often means layers upon layers of abstractions, DTOs, mappers, and repositories - all trying to "protect" the domain from the database.

But what if the database *is* the best place for your business logic?

PostgreSQL is not just storage. It's a powerful computation engine with transactions, constraints, triggers, functions, and decades of optimization. NpgsqlRest flips the script: instead of abstracting away the database, it puts PostgreSQL at the center and generates everything else.

- **Schema as contract** - Your tables, views, and functions become REST endpoints. One source of truth, zero drift.
- **SQL comments as config** - Routes, auth rules, caching - all declared where the logic lives.
- **Types flow outward** - PostgreSQL types generate TypeScript clients automatically. No manual mappings.
- **No middle tier** - No ORM impedance mismatch, no N+1 queries, no controller boilerplate.

## Installation

| Method | Command |
|--------|---------|
| **NPM** | `npm i npgsqlrest` |
| **Docker** | `docker pull vbilopav/npgsqlrest:latest` |
| **Direct Download** | [Releases](https://github.com/NpgsqlRest/NpgsqlRest/releases) |
| **.NET Library** | `dotnet add package NpgsqlRest` |

## Requirements

- PostgreSQL >= 13
- No runtime dependencies for native executables

## Documentation

For complete documentation including configuration options, authentication setup, TypeScript generation, and more, visit **[npgsqlrest.github.io](https://npgsqlrest.github.io/)**

## Contributing

Contributions are welcome. Please open a pull request with a description of your changes.

## License

MIT License
