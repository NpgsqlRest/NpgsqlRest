# NpgsqlRest

[![npm version](https://badge.fury.io/js/npgsqlrest.svg)](https://www.npmjs.com/package/npgsqlrest)
[![Build, Test, Publish](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml)
![License](https://img.shields.io/badge/license-MIT-green)

**Automatic REST API Server for PostgreSQL**

Transform your PostgreSQL database into a production-ready REST API server with automatic TypeScript code generation and end-to-end type safety.

**[Documentation](https://npgsqlrest.github.io/)** | **[Getting Started](https://npgsqlrest.github.io/getting-started/)** | **[Configuration](https://npgsqlrest.github.io/config-reference/)** | **[Annotations](https://npgsqlrest.github.io/comment-annotations/)**

## Installation

```bash
npm install npgsqlrest
```

This downloads the appropriate native executable for your platform (Windows, macOS, or Linux).

## Quick Start

1. **Create a PostgreSQL function with annotation:**

```sql
create function hello_world()
returns text
language sql
as $$
select 'Hello World'
$$;

comment on function hello_world() is 'HTTP GET /hello';
```

2. **Create configuration file** (`appsettings.json`):

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres"
  }
}
```

3. **Run the server:**

```bash
npx npgsqlrest
```

Your API is now live at `http://localhost:8080/hello`

## Usage

### Run with npx

```bash
npx npgsqlrest
```

### Run directly (after install)

```bash
./node_modules/.bin/npgsqlrest
```

### With custom config

```bash
npx npgsqlrest myconfig.json
```

### Override config via CLI

```bash
npx npgsqlrest --urls=http://localhost:3000
npx npgsqlrest --log:minimallevels:npgsqlrest=debug
```

### Show help

```bash
npx npgsqlrest --help
```

### Show version

```bash
npx npgsqlrest --version
```

## TypeScript Code Generation

Enable automatic TypeScript client generation:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres"
  },
  "NpgsqlRest": {
    "ClientCodeGen": {
      "Enabled": true,
      "FilePath": "./src/api/{0}Api.ts"
    }
  }
}
```

On startup, NpgsqlRest generates type-safe fetch functions for all your endpoints:

```typescript
// Auto-generated
export async function publicHelloWorld(): Promise<{status: number, response: string}> {
    const response = await fetch(baseUrl + "/hello", { method: "GET" });
    return { status: response.status, response: await response.text() };
}
```

## Key Features

- **Instant API Generation** - REST endpoints from functions, procedures, tables, and views
- **Declarative Configuration** - Configure endpoints via SQL comment annotations
- **TypeScript/JavaScript Generation** - Auto-generate frontend code with full type safety
- **High Performance** - AOT-compiled native executable, [6x faster than PostgREST](https://npgsqlrest.github.io/blog/postgresql-rest-api-benchmark-2025.html)
- **RESTful Path Parameters** - Routes like `/products/{id}` with URL parameter extraction
- **Authentication** - Cookie auth, JWT, Bearer tokens, OAuth (Google, GitHub, etc.)
- **Authorization** - Role-based access control with PostgreSQL integration
- **Caching** - In-memory, Redis, or HybridCache with stampede protection
- **Rate Limiting** - Built-in rate limiter with multiple policies
- **Server-Sent Events** - Real-time streaming via PostgreSQL RAISE INFO
- **Reverse Proxy** - Forward requests to upstream services
- **OpenAPI 3.0** - Auto-generated API documentation

## Configuration Example

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres"
  },
  "Urls": "http://localhost:8080",
  "Log": {
    "MinimalLevels": {
      "NpgsqlRest": "Debug"
    }
  },
  "Auth": {
    "CookieAuth": true
  },
  "NpgsqlRest": {
    "ClientCodeGen": {
      "Enabled": true,
      "FilePath": "./src/api/{0}Api.ts"
    },
    "HttpFileOptions": {
      "Enabled": true,
      "NamePattern": "./http/{0}_{1}.http"
    }
  }
}
```

For complete configuration options, see [Configuration Reference](https://npgsqlrest.github.io/config-reference/).

## Comment Annotations

Control endpoint behavior directly in SQL:

```sql
comment on function my_func() is '
HTTP GET /api/resource/{id}
@authorize admin, user
@cached
@timeout 30s
Content-Type: application/json
';
```

See [Annotation Guide](https://npgsqlrest.github.io/comment-annotations/) for all available annotations.

## Supported Platforms

The npm package includes native executables for:

- **Windows** - x64
- **macOS** - x64, ARM64 (Apple Silicon)
- **Linux** - x64, ARM64

## Alternative Installation Methods

| Method | Command |
|--------|---------|
| **Docker** | `docker pull vbilopav/npgsqlrest:latest` |
| **Direct Download** | [GitHub Releases](https://github.com/NpgsqlRest/NpgsqlRest/releases) |
| **.NET Library** | `dotnet add package NpgsqlRest` |

## Requirements

- PostgreSQL >= 13
- Node.js >= 14 (for npm/npx)

## Links

- [Documentation](https://npgsqlrest.github.io/)
- [Getting Started Guide](https://npgsqlrest.github.io/getting-started/)
- [Configuration Reference](https://npgsqlrest.github.io/config-reference/)
- [Comment Annotations](https://npgsqlrest.github.io/comment-annotations/)
- [GitHub Repository](https://github.com/NpgsqlRest/NpgsqlRest)
- [Changelog](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog.md)
- [Docker Hub](https://hub.docker.com/r/vbilopav/npgsqlrest)

## License

MIT License - see [LICENSE](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/LICENSE)
