using System.Text.Json.Nodes;

namespace NpgsqlRest.Common;

/// <summary>
/// Maps a PostgreSQL <see cref="TypeDescriptor"/> to a JSON Schema fragment (type + format). Shared
/// by the OpenApi and Mcp plugins via linked source (internal, own copy per assembly). AOT-safe —
/// builds System.Text.Json.Nodes only.
/// </summary>
internal static class SchemaMapper
{
    public static JsonObject GetSchemaForType(TypeDescriptor type)
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
}
