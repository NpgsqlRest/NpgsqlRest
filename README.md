# NpgsqlRest

[![Build, Test, Publish and Release](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml/badge.svg)](https://github.com/NpgsqlRest/NpgsqlRest/actions/workflows/build-test-publish.yml)
![License](https://img.shields.io/badge/license-MIT-green)
![GitHub Stars](https://img.shields.io/github/stars/NpgsqlRest/NpgsqlRest?style=social)
![GitHub Forks](https://img.shields.io/github/forks/NpgsqlRest/NpgsqlRest?style=social)

**Automatic REST API Server for PostgreSQL**

> Transform your PostgreSQL database into a production-ready REST API server with automatic TypeScript code generation and end-to-end type safety.

**[Documentation](https://npgsqlrest.github.io/)** | **[Getting Started](https://npgsqlrest.github.io/getting-started/)** | **[Configuration Reference](https://npgsqlrest.github.io/config-reference/)** | **[Annotation Guide](https://npgsqlrest.github.io/comment-annotations/)**

## Key Features

- **Instant API Generation** - Automatically creates REST endpoints from PostgreSQL functions, procedures, tables, and views
- **Declarative Configuration** - Configure endpoints directly in your database using comment annotations
- **TypeScript Code Generation** - Auto-generate type-safe frontend code with full static type checking
- **Native Executables** - AOT-compiled binaries with zero dependencies and instant startup
- **RESTful Path Parameters** - Define routes like `/products/{id}` with parameters extracted from URL paths
- **Reverse Proxy Endpoints** - Forward requests to upstream services with passthrough or transform modes
- **HTTP Custom Types** - Enable PostgreSQL functions to call external APIs via annotated composite types
- **Enterprise Ready** - Authentication, authorization, rate limiting, caching, SSE streaming, OpenAPI 3.0, and more

## Quick Start

### 1. Annotate a PostgreSQL Function

```sql
create function hello(_name text)
returns text
language sql as $$
select 'Hello, ' || _name
$$;

comment on function hello(text) is 'HTTP GET /hello';
```

### 2. Configure Connection

Create `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=my_db;Username=postgres;Password=postgres"
  }
}
```

### 3. Run

```bash
# Download from releases, or:
npm i npgsqlrest && npx npgsqlrest

# Or with Docker:
docker run -p 8080:8080 -v ./appsettings.json:/app/appsettings.json vbilopav/npgsqlrest:latest
```

Your API is now live at `http://localhost:8080/hello?name=World`

## Installation Options

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
