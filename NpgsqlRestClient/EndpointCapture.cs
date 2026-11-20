using System.Text.Json;
using NpgsqlRest;

namespace NpgsqlRestClient;

/// <summary>
/// Captures endpoint metadata during NpgsqlRest initialization.
/// Used by the --endpoints CLI command to output endpoint information.
/// </summary>
public class EndpointCapture : IEndpointCreateHandler
{
    public static RoutineEndpoint[] Endpoints { get; private set; } = [];

    public void Cleanup(RoutineEndpoint[] endpoints)
    {
        Endpoints = endpoints;
    }

    private static void WriteLines(Utf8JsonWriter writer, string name, string[]? lines)
    {
        if (lines is not { Length: > 0 })
        {
            return;
        }
        writer.WriteStartArray(name);
        foreach (var line in lines) writer.WriteStringValue(line);
        writer.WriteEndArray();
    }

    public static void WriteEndpointsJson(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();

        foreach (var ep in Endpoints)
        {
            writer.WriteStartObject();

            writer.WriteString("method", ep.Method.ToString());
            writer.WriteString("path", ep.Path);

            // Routine info
            writer.WriteStartObject("routine");
            writer.WriteString("schema", ep.Routine.Schema);
            writer.WriteString("name", ep.Routine.Name);
            writer.WriteString("type", ep.Routine.Type.ToString());
            if (ep.Routine.Type is RoutineType.Table or RoutineType.View)
            {
                writer.WriteString("crudType", ep.Routine.CrudType.ToString());
            }
            writer.WriteBoolean("returnsSet", ep.Routine.ReturnsSet);
            writer.WriteBoolean("isVoid", ep.Routine.IsVoid);
            writer.WriteString("definition", ep.Routine.SimpleDefinition);
            // Where it comes from, so that a tool can lead back to it: the file of a SQL file endpoint.
            // (A database routine is found by its schema, name and definition above.)
            if (ep.Routine.SourceFile is not null)
            {
                writer.WriteString("sourceFile", ep.Routine.SourceFile);
            }
            writer.WriteEndObject();

            // What was read from the comment.
            WriteLines(writer, "annotations", ep.AnnotationLines);
            // Prose: the lines that are no annotation. OpenAPI and MCP use them as the description.
            WriteLines(writer, "commentLines", ep.UnhandledCommentLines);
            // Lines written as annotations (@name ...) that nothing reads: a misspelt name, another tool's line.
            WriteLines(writer, "unknownAnnotations", ep.UnknownAnnotationLines);
            // Warnings and errors logged about this endpoint while its comment was parsed.
            WriteLines(writer, "diagnostics", ep.Diagnostics);

            // Parameters
            writer.WriteStartArray("parameters");
            foreach (var param in ep.Routine.Parameters)
            {
                writer.WriteStartObject();
                writer.WriteString("name", param.ConvertedName);
                writer.WriteString("originalName", param.ActualName);
                writer.WriteString("type", param.TypeDescriptor.OriginalType);
                writer.WriteBoolean("hasDefault", param.TypeDescriptor.HasDefault);
                if (ep.HasPathParameters && ep.PathParameters!.Contains(param.ConvertedName, StringComparer.OrdinalIgnoreCase))
                {
                    writer.WriteBoolean("isPathParameter", true);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            // Return columns
            if (ep.Routine.ColumnCount > 0)
            {
                writer.WriteStartArray("returnColumns");
                for (int i = 0; i < ep.Routine.ColumnCount; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", ep.Routine.ColumnNames[i]);
                    writer.WriteString("originalName", ep.Routine.OriginalColumnNames[i]);
                    if (i < ep.Routine.ColumnsTypeDescriptor.Length)
                    {
                        writer.WriteString("type", ep.Routine.ColumnsTypeDescriptor[i].OriginalType);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }

            // Configuration
            writer.WriteString("requestParamType", ep.RequestParamType.ToString());
            writer.WriteBoolean("requiresAuthorization", ep.RequiresAuthorization);

            if (ep.AuthorizeRoles is { Count: > 0 })
            {
                writer.WriteStartArray("authorizeRoles");
                foreach (var role in ep.AuthorizeRoles) writer.WriteStringValue(role);
                writer.WriteEndArray();
            }

            if (ep.Login) writer.WriteBoolean("login", true);
            if (ep.Logout) writer.WriteBoolean("logout", true);
            if (ep.SecuritySensitive) writer.WriteBoolean("securitySensitive", true);
            if (ep.Raw) writer.WriteBoolean("raw", true);
            if (ep.Cached) writer.WriteBoolean("cached", true);
            if (ep.Upload) writer.WriteBoolean("upload", true);
            if (ep.IsProxy) writer.WriteBoolean("isProxy", true);
            if (ep.UserContext) writer.WriteBoolean("userContext", true);
            if (ep.UseUserParameters) writer.WriteBoolean("userParameters", true);
            if (ep.ConnectionName is not null) writer.WriteString("connectionName", ep.ConnectionName);
            if (ep.SseEventsPath is not null) writer.WriteString("sseEventsPath", ep.SseEventsPath);
            if (ep.RateLimiterPolicy is not null) writer.WriteString("rateLimiterPolicy", ep.RateLimiterPolicy);
            if (ep.ErrorCodePolicy is not null) writer.WriteString("errorCodePolicy", ep.ErrorCodePolicy);

            if (ep.CustomParameters is { Count: > 0 })
            {
                writer.WriteStartObject("customParameters");
                foreach (var kvp in ep.CustomParameters)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }
                writer.WriteEndObject();
            }

            // Custom parameters (key = value) that nothing reads, written with or without the @.
            if (ep.CustomParameters is { Count: > 0 })
            {
                List<string>? unknown = null;
                foreach (var key in ep.CustomParameters.Keys)
                {
                    if (CommentAnnotations.IsKnownCustomParameter(key) is false)
                    {
                        (unknown ??= []).Add(key);
                    }
                }
                if (unknown is not null)
                {
                    writer.WriteStartArray("unknownCustomParameters");
                    foreach (var key in unknown) writer.WriteStringValue(key);
                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
