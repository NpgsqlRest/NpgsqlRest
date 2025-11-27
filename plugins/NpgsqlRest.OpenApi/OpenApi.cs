using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Npgsql;
using static NpgsqlRest.NpgsqlRestOptions;

namespace NpgsqlRest.OpenAPI;

[JsonSerializable(typeof(JsonObject))]
internal partial class OpenApiSerializerContext : JsonSerializerContext;

public class OpenApi(OpenApiOptions openApiOptions) : IEndpointCreateHandler
{
    public OpenApi() : this(new OpenApiOptions()) { }

    private IApplicationBuilder _builder = default!;
    private JsonObject _document = default!;
    private JsonObject _paths = default!;
    private JsonObject _schemas = default!;
    private JsonObject? _securitySchemes = null;

    public void Setup(IApplicationBuilder builder, NpgsqlRestOptions options)
    {
        _builder = builder;

        // Initialize OpenAPI document structure
        var info = new JsonObject
        {
            ["title"] = GetDocumentTitle(),
            ["version"] = openApiOptions.DocumentVersion
        };

        if (!string.IsNullOrEmpty(openApiOptions.DocumentDescription))
        {
            info["description"] = openApiOptions.DocumentDescription;
        }

        _document = new JsonObject
        {
            ["openapi"] = "3.0.3",
            ["info"] = info
        };

        // Add servers section if configured
        var servers = BuildServersArray();
        if (servers != null && servers.Count > 0)
        {
            _document["servers"] = servers;
        }

        _paths = new JsonObject();
        _document["paths"] = _paths;

        _schemas = new JsonObject();
        _document["components"] = new JsonObject
        {
            ["schemas"] = _schemas
        };

        // Initialize security schemes if configured
        if (openApiOptions.SecuritySchemes != null && openApiOptions.SecuritySchemes.Length > 0)
        {
            _securitySchemes = BuildSecuritySchemes(openApiOptions.SecuritySchemes);
            if (_securitySchemes != null && _securitySchemes.Count > 0)
            {
                _document["components"]!["securitySchemes"] = _securitySchemes;
            }
        }
    }

    public void Handle(RoutineEndpoint endpoint)
    {
        var path = endpoint.Path;
        var method = endpoint.Method.ToString().ToLowerInvariant();

        // Initialize path object if it doesn't exist
        if (!_paths.ContainsKey(path))
        {
            _paths[path] = new JsonObject();
        }

        var pathItem = _paths[path] as JsonObject;
        var operation = new JsonObject();

        // Add operation summary and description
        if (!string.IsNullOrEmpty(endpoint.Routine.Comment))
        {
            var lines = endpoint.Routine.Comment.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            operation["summary"] = lines[0].Trim();
            if (lines.Length > 1)
            {
                operation["description"] = string.Join("\n", lines.Skip(1).Select(l => l.Trim()));
            }
        }
        else
        {
            operation["summary"] = $"{endpoint.Routine.Type} {endpoint.Routine.Schema}.{endpoint.Routine.Name}";
        }

        // Add tags based on schema
        operation["tags"] = new JsonArray(endpoint.Routine.Schema);

        // Add operation ID
        operation["operationId"] = $"{endpoint.Routine.Schema}_{endpoint.Routine.Name}_{method}";

        // Add parameters
        var parameters = new JsonArray();

        if (endpoint.Routine.Parameters.Length > 0)
        {
            if (endpoint.RequestParamType == RequestParamType.QueryString)
            {
                foreach (var param in endpoint.Routine.Parameters)
                {
                    // Skip body parameter if it exists
                    if (endpoint.BodyParameterName is not null &&
                        (string.Equals(param.ConvertedName, endpoint.BodyParameterName, StringComparison.Ordinal) ||
                         string.Equals(param.ActualName, endpoint.BodyParameterName, StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    var parameter = new JsonObject
                    {
                        ["name"] = param.ConvertedName,
                        ["in"] = "query",
                        ["required"] = !param.TypeDescriptor.HasDefault,
                        ["schema"] = GetSchemaForType(param.TypeDescriptor)
                    };

                    parameters.Add((JsonNode)parameter);
                }
            }
            else if (endpoint.RequestParamType == RequestParamType.BodyJson)
            {
                // Add request body for JSON
                var requestSchema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject()
                };

                var properties = requestSchema["properties"] as JsonObject;
                var required = new JsonArray();

                foreach (var param in endpoint.Routine.Parameters)
                {
                    properties![param.ConvertedName] = GetSchemaForType(param.TypeDescriptor);
                    if (!param.TypeDescriptor.HasDefault)
                    {
                        required.Add((JsonNode?)JsonValue.Create(param.ConvertedName));
                    }
                }

                if (required.Count > 0)
                {
                    requestSchema["required"] = required;
                }

                operation["requestBody"] = new JsonObject
                {
                    ["required"] = true,
                    ["content"] = new JsonObject
                    {
                        ["application/json"] = new JsonObject
                        {
                            ["schema"] = requestSchema
                        }
                    }
                };
            }
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        // Handle body parameter in query string mode
        if (endpoint.BodyParameterName is not null && endpoint.RequestParamType == RequestParamType.QueryString)
        {
            var bodyParam = endpoint.Routine.Parameters
                .FirstOrDefault(p =>
                    string.Equals(p.ConvertedName, endpoint.BodyParameterName, StringComparison.Ordinal) ||
                    string.Equals(p.ActualName, endpoint.BodyParameterName, StringComparison.Ordinal));

            if (bodyParam is not null)
            {
                operation["requestBody"] = new JsonObject
                {
                    ["required"] = !bodyParam.TypeDescriptor.HasDefault,
                    ["content"] = new JsonObject
                    {
                        ["text/plain"] = new JsonObject
                        {
                            ["schema"] = GetSchemaForType(bodyParam.TypeDescriptor)
                        }
                    }
                };
            }
        }

        // Add responses
        var responses = new JsonObject
        {
            ["200"] = new JsonObject
            {
                ["description"] = "Successful response"
            }
        };

        // Add response content if not void
        if (!endpoint.Routine.IsVoid)
        {
            var responseContent = new JsonObject();
            var contentType = endpoint.ResponseContentType ?? "application/json";

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var responseSchema = GetResponseSchema(endpoint.Routine);
                responseContent[contentType] = new JsonObject
                {
                    ["schema"] = responseSchema
                };
            }
            else
            {
                // For non-JSON responses
                responseContent[contentType] = new JsonObject
                {
                    ["schema"] = new JsonObject
                    {
                        ["type"] = "string"
                    }
                };
            }

            (responses["200"] as JsonObject)!["content"] = responseContent;
        }

        operation["responses"] = responses;

        // Add security if required
        if (endpoint.RequiresAuthorization)
        {
            // Add security requirements for this operation
            if (_securitySchemes != null && _securitySchemes.Count > 0)
            {
                // Add all configured security schemes as alternatives (OR relationship)
                var securityArray = new JsonArray();
                foreach (var schemeName in _securitySchemes.AsObject().Select(kv => kv.Key))
                {
                    securityArray.Add((JsonNode)new JsonObject
                    {
                        [schemeName] = new JsonArray()
                    });
                }
                operation["security"] = securityArray;
            }
            else
            {
                // Add default bearer auth if no schemes configured
                operation["security"] = new JsonArray(
                    new JsonObject
                    {
                        ["bearerAuth"] = new JsonArray()
                    }
                );

                // Add default bearer scheme to components if not already there
                if (!_document["components"]!.AsObject().ContainsKey("securitySchemes"))
                {
                    _document["components"]!["securitySchemes"] = new JsonObject
                    {
                        ["bearerAuth"] = new JsonObject
                        {
                            ["type"] = "http",
                            ["scheme"] = "bearer",
                            ["bearerFormat"] = "JWT"
                        }
                    };
                }
            }
        }

        pathItem![method] = operation;
    }

    public void Cleanup()
    {
        if (openApiOptions.FileName is null && openApiOptions.UrlPath is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(_document, OpenApiSerializerContext.Default.JsonObject);

        // Write to file if FileName is specified
        if (openApiOptions.FileName is not null)
        {
            var fullFileName = System.IO.Path.Combine(Environment.CurrentDirectory, openApiOptions.FileName);

            if (!openApiOptions.FileOverwrite && File.Exists(fullFileName))
            {
                Logger?.LogDebug("OpenAPI file already exists and FileOverwrite is false: {fileName}", fullFileName);
            }
            else
            {
                var dir = System.IO.Path.GetDirectoryName(fullFileName);
                if (dir is not null && Directory.Exists(dir) is false)
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullFileName, json);
                Logger?.LogDebug("Created OpenAPI file: {fileName}", fullFileName);
            }
        }

        // Serve as endpoint if UrlPath is specified
        if (openApiOptions.UrlPath is not null)
        {
            var path = openApiOptions.UrlPath;
            _builder.Use(async (context, next) =>
            {
                if (string.Equals(context.Request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(context.Request.Path, path, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(json);
                    return;
                }

                await next(context);
            });

            var host = GetHost();
            Logger?.LogDebug("Exposed OpenAPI document on URL: {host}{path}", host, path);
        }
    }

    private JsonObject GetSchemaForType(TypeDescriptor type)
    {
        var schema = new JsonObject();

        if (type.IsArray)
        {
            schema["type"] = "array";
            var itemType = new TypeDescriptor(type.Type, type.HasDefault);
            schema["items"] = GetSchemaForType(itemType);
            return schema;
        }

        if (type.IsNumeric)
        {
            if (type.Type.Contains("int", StringComparison.OrdinalIgnoreCase))
            {
                schema["type"] = "integer";
                if (type.Type.Contains("big", StringComparison.OrdinalIgnoreCase) ||
                    type.Type == "int8")
                {
                    schema["format"] = "int64";
                }
                else
                {
                    schema["format"] = "int32";
                }
            }
            else
            {
                schema["type"] = "number";
                if (type.Type == "real" || type.Type == "float4")
                {
                    schema["format"] = "float";
                }
                else if (type.Type == "double precision" || type.Type == "float8")
                {
                    schema["format"] = "double";
                }
            }
            return schema;
        }

        if (type.IsBoolean)
        {
            schema["type"] = "boolean";
            return schema;
        }

        if (type.IsDateTime)
        {
            schema["type"] = "string";
            schema["format"] = "date-time";
            return schema;
        }

        if (type.IsDate)
        {
            schema["type"] = "string";
            schema["format"] = "date";
            return schema;
        }

        if (type.Type == "uuid")
        {
            schema["type"] = "string";
            schema["format"] = "uuid";
            return schema;
        }

        if (type.IsJson)
        {
            schema["type"] = "object";
            return schema;
        }

        // Default to string
        schema["type"] = "string";
        return schema;
    }

    private JsonObject GetResponseSchema(Routine routine)
    {
        if (routine.ReturnsSet)
        {
            // Returns array of objects
            var itemSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };

            var properties = itemSchema["properties"] as JsonObject;
            for (int i = 0; i < routine.ColumnCount; i++)
            {
                properties![routine.ColumnNames[i]] = GetSchemaForType(routine.ColumnsTypeDescriptor[i]);
            }

            return new JsonObject
            {
                ["type"] = "array",
                ["items"] = itemSchema
            };
        }
        else if (routine.ColumnCount > 1 || routine.ReturnsRecordType)
        {
            // Returns single object
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            };

            var properties = schema["properties"] as JsonObject;
            for (int i = 0; i < routine.ColumnCount; i++)
            {
                properties![routine.ColumnNames[i]] = GetSchemaForType(routine.ColumnsTypeDescriptor[i]);
            }

            return schema;
        }
        else if (routine.ColumnCount == 1)
        {
            // Returns single value
            return GetSchemaForType(routine.ColumnsTypeDescriptor[0]);
        }

        // Default
        return new JsonObject
        {
            ["type"] = "object"
        };
    }

    private string GetHost()
    {
        string? host = null;
        if (_builder is WebApplication app)
        {
            if (app.Urls.Count != 0)
            {
                host = app.Urls.FirstOrDefault();
            }
            else
            {
                var section = app.Configuration?.GetSection("ASPNETCORE_URLS");
                if (section?.Value is not null)
                {
                    host = section.Value.Split(";")?.LastOrDefault();
                }
            }
            if (host is null && app.Configuration?["urls"] is not null)
            {
                host = app.Configuration?["urls"];
            }
        }
        // default, assumed host
        host ??= "http://localhost:8080";
        return host.TrimEnd('/');
    }
    
    private string GetDocumentTitle()
    {
        if (openApiOptions.DocumentTitle is not null)
        {
            return openApiOptions.DocumentTitle;
        }
        if (openApiOptions.ConnectionString is not null)
        {
            return new NpgsqlConnectionStringBuilder(openApiOptions.ConnectionString).Database ??
                   (openApiOptions.ConnectionString?.Split(";") ?? []).FirstOrDefault(s => s.StartsWith("Database="))
                   ?.Split("=")?.Last() ?? "NpgsqlRest API";
        }
        return "NpgsqlRest API";
    }

    private JsonArray? BuildServersArray()
    {
        var serversArray = new JsonArray();

        // Add configured servers first
        if (openApiOptions.Servers != null)
        {
            foreach (var server in openApiOptions.Servers)
            {
                var serverObj = new JsonObject
                {
                    ["url"] = server.Url
                };

                if (!string.IsNullOrEmpty(server.Description))
                {
                    serverObj["description"] = server.Description;
                }

                serversArray.Add((JsonNode)serverObj);
            }
        }

        // Add current server if enabled and not already added
        if (openApiOptions.AddCurrentServer)
        {
            var currentHost = GetHost();

            // Check if this URL is already in the servers array
            var alreadyExists = openApiOptions.Servers?.Any(s =>
                string.Equals(s.Url.TrimEnd('/'), currentHost, StringComparison.OrdinalIgnoreCase)) ?? false;

            if (!alreadyExists)
            {
                var currentServerObj = new JsonObject
                {
                    ["url"] = currentHost
                };

                // Add description for current server
                if (currentHost.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    currentServerObj["description"] = "Development server";
                }

                serversArray.Add((JsonNode)currentServerObj);
            }
        }

        return serversArray.Count > 0 ? serversArray : null;
    }

    private JsonObject? BuildSecuritySchemes(OpenApiSecurityScheme[] schemes)
    {
        if (schemes == null || schemes.Length == 0)
        {
            return null;
        }

        var securitySchemes = new JsonObject();

        foreach (var scheme in schemes)
        {
            var schemeObj = new JsonObject();

            switch (scheme.Type)
            {
                case OpenApiSecuritySchemeType.Http:
                    schemeObj["type"] = "http";
                    if (scheme.Scheme.HasValue)
                    {
                        schemeObj["scheme"] = scheme.Scheme.Value.ToString().ToLowerInvariant();
                    }
                    if (!string.IsNullOrEmpty(scheme.BearerFormat))
                    {
                        schemeObj["bearerFormat"] = scheme.BearerFormat;
                    }
                    break;

                case OpenApiSecuritySchemeType.ApiKey:
                    schemeObj["type"] = "apiKey";
                    if (!string.IsNullOrEmpty(scheme.In))
                    {
                        schemeObj["name"] = scheme.In;
                    }
                    if (scheme.ApiKeyLocation.HasValue)
                    {
                        schemeObj["in"] = scheme.ApiKeyLocation.Value.ToString().ToLowerInvariant();
                    }
                    break;
            }

            if (!string.IsNullOrEmpty(scheme.Description))
            {
                schemeObj["description"] = scheme.Description;
            }

            securitySchemes[scheme.Name] = schemeObj;
        }

        return securitySchemes.Count > 0 ? securitySchemes : null;
    }
}