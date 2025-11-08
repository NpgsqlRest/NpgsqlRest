# NpgsqlRest.OpenAPI

**Automatic OpenAPI Documentation Generation for NpgsqlRest**

**Metadata plug-in** for the `NpgsqlRest` library.

Provides support for the generation of **[OpenAPI 3.0](https://spec.openapis.org/oas/v3.0.3)** documentation.

## Overview

Generates OpenAPI 3.0 specification documents for all endpoints created by NpgsqlRest. The generated documentation includes:

- Path definitions for all REST endpoints
- Request parameters (query string or JSON body)
- Request body schemas
- Response schemas
- Security definitions
- Tags based on database schemas

The OpenAPI document can be:
- Written to a JSON file on disk
- Served as a live endpoint
- Both written to disk and served as an endpoint

Example of generated OpenAPI document structure:

```json
{
  "openapi": "3.0.3",
  "info": {
    "title": "NpgsqlRest API",
    "version": "1.0.0"
  },
  "paths": {
    "/api/hello-world": {
      "post": {
        "summary": "Function public.hello_world",
        "tags": ["public"],
        "operationId": "public_hello_world_post",
        "responses": {
          "200": {
            "description": "Successful response",
            "content": {
              "application/json": {
                "schema": {
                  "type": "string"
                }
              }
            }
          }
        }
      }
    }
  }
}
```

## Install

```console
dotnet add package NpgsqlRest.OpenAPI --version 1.0.0
```

## Minimal Usage

Initialize `EndpointCreateHandlers` options property as an array containing an `OpenApi` plug-in instance:

```csharp
using NpgsqlRest;
using NpgsqlRest.OpenAPI;

var app = builder.Build();
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [new OpenApi(OpenApiOptions.CreateBoth())],
});
app.Run();
```

This will generate an `openapi.json` file in the current directory and serve the OpenAPI document at `/openapi.json`.

## OpenAPI Options

### Using Factory Methods (Recommended)

```csharp
using NpgsqlRest;
using NpgsqlRest.OpenAPI;

var app = builder.Build();
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [
        // Generate file only
        new OpenApi(OpenApiOptions.CreateFile("openapi.json")),

        // Serve as endpoint only
        new OpenApi(OpenApiOptions.CreateEndpoint("/openapi.json")),

        // Both file and endpoint
        new OpenApi(OpenApiOptions.CreateBoth("openapi.json", "/openapi.json"))
    ],
});
app.Run();
```

### Using Full Configuration

```csharp
using NpgsqlRest;
using NpgsqlRest.OpenAPI;

var app = builder.Build();
app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [
        new OpenApi(new OpenApiOptions
        {
            // The file name to use for the OpenAPI document.
            // If null, no file will be generated.
            // You can use a relative path (e.g., "docs/openapi.json")
            FileName = "openapi.json",

            // The path to serve the OpenAPI document at.
            // If null, the document will not be served.
            // Example: "/openapi.json" or "/api/docs/openapi.json"
            Path = "/openapi.json",

            // Set to true to overwrite existing files. Default is false.
            FileOverwrite = false,

            // The title of the OpenAPI document.
            // This appears in the "info" section of the OpenAPI specification.
            DocumentTitle = "My REST API",

            // The version of the OpenAPI document.
            // This appears in the "info" section of the OpenAPI specification.
            DocumentVersion = "1.0.0",

            // Optional description of the API.
            // This appears in the "info" section of the OpenAPI specification.
            DocumentDescription = "REST API generated from PostgreSQL database"
        })
    ],
});
app.Run();
```

## Integration with Swagger UI

You can use the generated OpenAPI document with Swagger UI for interactive API documentation:

```csharp
using NpgsqlRest;
using NpgsqlRest.OpenAPI;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseNpgsqlRest(new()
{
    ConnectionString = connectionString,
    EndpointCreateHandlers = [
        new OpenApi(OpenApiOptions.CreateEndpoint("/openapi.json"))
    ],
});

app.UseSwagger(c =>
{
    c.RouteTemplate = "swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/openapi.json", "NpgsqlRest API");
});

app.Run();
```

## Features

- **Complete OpenAPI 3.0 Specification**: Generates valid OpenAPI 3.0.3 documents
- **Type Mapping**: Automatically maps PostgreSQL types to OpenAPI/JSON Schema types
- **Request Parameters**: Supports both query string and JSON body parameters
- **Response Schemas**: Generates schemas for all return types (single values, objects, arrays)
- **Security**: Adds security definitions for endpoints requiring authorization
- **Tags**: Organizes endpoints by database schema
- **Comments**: Includes PostgreSQL function/procedure comments as operation descriptions
- **Arrays**: Properly handles PostgreSQL array types
- **Flexible Output**: Save to file, serve as endpoint, or both

## Library Dependencies

- NpgsqlRest (>= 3.0.0)

## Contributing

Contributions from the community are welcomed.
Please make a pull request with a description if you wish to contribute.

## License

This project is licensed under the terms of the MIT license.
