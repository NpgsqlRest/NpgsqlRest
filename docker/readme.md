# NpgsqlRest Docker Images

[![Docker Pulls](https://img.shields.io/docker/pulls/vbilopav/npgsqlrest)](https://hub.docker.com/r/vbilopav/npgsqlrest)

Transform your PostgreSQL database into a REST API server with zero code. NpgsqlRest automatically creates REST endpoints from PostgreSQL functions, procedures, tables, and views.

## Quick Start

```bash
# Create minimal config
cat > appsettings.json << 'EOF'
{
  "ConnectionStrings": {
    "Default": "Host=host.docker.internal;Port=5432;Database=mydb;Username=postgres;Password=postgres"
  }
}
EOF

# Run the container
docker run -p 8080:8080 -v ./appsettings.json:/app/appsettings.json vbilopav/npgsqlrest
```

Your REST API is now live at `http://localhost:8080`

## Available Tags

| Tag | Description | Size |
|-----|-------------|------|
| `latest` | AOT-compiled native executable (x64) | ~80 MB |
| `latest-arm` | AOT-compiled native executable (ARM64) | ~80 MB |
| `latest-jit` | .NET 10 JIT runtime | ~250 MB |
| `latest-bun` | AOT executable + Bun runtime | ~120 MB |

Version-specific tags are also available (e.g., `3.4.1`, `3.4.1-arm`, `3.4.1-jit`, `3.4.1-bun`).

### Which tag should I use?

- **`latest`** - Best for production on x64 systems. Fastest startup, smallest size.
- **`latest-arm`** - For ARM64 systems (Raspberry Pi, AWS Graviton, Apple Silicon VMs).
- **`latest-jit`** - When you need .NET runtime features or faster execution for long-running requests.
- **`latest-bun`** - When using proxy endpoints that execute Bun/JavaScript scripts.

## Usage Examples

### Basic Usage

```bash
docker run -p 8080:8080 \
  -v ./appsettings.json:/app/appsettings.json \
  vbilopav/npgsqlrest
```

### With Host Network (for local PostgreSQL)

```bash
docker run -p 8080:8080 \
  --network host \
  -v ./appsettings.json:/app/appsettings.json \
  vbilopav/npgsqlrest
```

### With Environment Variables

```bash
docker run -p 8080:8080 \
  -e ConnectionStrings__Default="Host=db;Database=mydb;Username=postgres;Password=secret" \
  vbilopav/npgsqlrest
```

### Docker Compose

```yaml
services:
  api:
    image: vbilopav/npgsqlrest:latest
    ports:
      - "8080:8080"
    volumes:
      - ./appsettings.json:/app/appsettings.json
    depends_on:
      - db

  db:
    image: postgres:17
    environment:
      POSTGRES_PASSWORD: postgres
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
```

### With SSL Certificates

```bash
docker run -p 8080:8080 -p 8443:8443 \
  -v ./appsettings.json:/app/appsettings.json \
  -v ./certs:/app/certs:ro \
  vbilopav/npgsqlrest
```

### Custom Configuration Path

```bash
docker run -p 8080:8080 \
  -v ./myconfig.json:/app/config.json \
  vbilopav/npgsqlrest config.json
```

## Configuration

The container looks for `appsettings.json` in `/app` by default. Mount your configuration file:

```bash
-v /path/to/your/appsettings.json:/app/appsettings.json
```

### Minimal Configuration

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=postgres"
  }
}
```

### Production Configuration Example

```json
{
  "ConnectionStrings": {
    "Default": "Host=db;Port=5432;Database=mydb;Username=app;Password=${DB_PASSWORD}"
  },
  "Config": {
    "ParseEnvironmentVariables": true
  },
  "Urls": "http://*:8080",
  "Ssl": {
    "UseHttps": true,
    "HttpsPort": 8443,
    "Certificate": "/app/certs/server.pfx",
    "CertificatePassword": "${CERT_PASSWORD}"
  },
  "Auth": {
    "CookieAuth": true,
    "CookieName": ".MyApp.Auth"
  },
  "Log": {
    "ToConsole": true,
    "MinimalLevels": {
      "Default": "Warning",
      "NpgsqlRest": "Information"
    }
  }
}
```

For the complete configuration reference, see [appsettings.json](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/NpgsqlRestClient/appsettings.json).

## Ports

| Port | Description |
|------|-------------|
| 8080 | Default HTTP port |
| 8443 | Default HTTPS port (when SSL enabled) |

## Volumes

| Path | Description |
|------|-------------|
| `/app/appsettings.json` | Main configuration file |
| `/app/certs/` | SSL certificates directory |
| `/app/wwwroot/` | Static files directory |
| `/app/keys/` | Data protection keys (for persistent auth tokens) |

## Environment Variables

Configuration can be overridden with environment variables using `__` as section separator:

```bash
# Override connection string
-e ConnectionStrings__Default="Host=db;..."

# Override log level
-e Log__MinimalLevels__NpgsqlRest="Debug"

# Override listening URL
-e Urls="http://*:5000"
```

## Health Check

The container exposes endpoints at the configured URL. You can configure a health check endpoint:

```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
  interval: 30s
  timeout: 10s
  retries: 3
```

## Links

- [GitHub Repository](https://github.com/NpgsqlRest/NpgsqlRest)
- [Documentation](https://npgsqlrest.github.io/)
- [Full Configuration Reference](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/NpgsqlRestClient/appsettings.json)
- [Changelog](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/changelog.md)

## License

MIT License - see [LICENSE](https://github.com/NpgsqlRest/NpgsqlRest/blob/master/LICENSE)
