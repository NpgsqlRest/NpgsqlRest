using System.Text.Json.Nodes;

namespace NpgsqlRestClient;

/// <summary>
/// Generates a JSON Schema (draft-07) describing the appsettings.json configuration structure.
/// Walks ConfigDefaults.GetDefaults() to infer types, defaults, and enum constraints.
/// </summary>
public static class ConfigSchemaGenerator
{
    /// <summary>
    /// Known enum fields mapped by their config path to valid string values.
    /// </summary>
    private static readonly Dictionary<string, string[]> EnumFields = new(StringComparer.OrdinalIgnoreCase)
    {
        // Config section
        ["Config:ValidateConfigKeys"] = ["Ignore", "Warning", "Error"],

        // DataProtection
        ["DataProtection:Storage"] = ["Default", "FileSystem", "Database"],
        ["DataProtection:KeyEncryption"] = ["None", "Certificate", "Dpapi"],

        // Log
        ["Log:ConsoleMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:FileMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:PostgresMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:OTLPMinimumLevel"] = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"],
        ["Log:OTLPProtocol"] = ["Grpc", "HttpProtobuf"],

        // ResponseCompression
        ["ResponseCompression:CompressionLevel"] = ["Optimal", "Fastest", "NoCompression", "SmallestSize"],

        // Auth:PasskeyAuth
        ["Auth:PasskeyAuth:UserVerificationRequirement"] = ["preferred", "required", "discouraged"],
        ["Auth:PasskeyAuth:ResidentKeyRequirement"] = ["preferred", "required", "discouraged"],
        ["Auth:PasskeyAuth:AttestationConveyance"] = ["none", "indirect", "direct", "enterprise"],

        // Auth:External:BasicAuth
        ["NpgsqlRest:AuthenticationOptions:BasicAuth:SslRequirement"] = ["Required", "NotRequired", "None"],

        // CacheOptions
        ["CacheOptions:Type"] = ["Memory", "Redis", "Hybrid"],

        // NpgsqlRest core
        ["NpgsqlRest:CommentsMode"] = ["Ignore", "ParseAll", "OnlyWithHttpTag"],
        ["NpgsqlRest:DefaultHttpMethod"] = ["GET", "PUT", "POST", "DELETE", "PATCH", "HEAD", "OPTIONS"],
        ["NpgsqlRest:DefaultRequestParamType"] = ["QueryString", "BodyJson"],
        ["NpgsqlRest:QueryStringNullHandling"] = ["Ignore", "EmptyString", "NullLiteral"],
        ["NpgsqlRest:TextResponseNullHandling"] = ["EmptyString", "NullLiteral", "NoContent"],
        ["NpgsqlRest:RequestHeadersMode"] = ["Ignore", "Context", "Parameter"],
        ["NpgsqlRest:LogConnectionNoticeEventsMode"] = ["FirstStackFrameAndMessage", "FullStackTrace", "MessageOnly"],
        ["NpgsqlRest:DefaultServerSentEventsEventNoticeLevel"] = ["DEBUG", "LOG", "INFO", "NOTICE", "WARNING"],

        // NpgsqlRest sub-options
        ["NpgsqlRest:HttpFileOptions:Option"] = ["File", "Console", "Both"],
        ["NpgsqlRest:HttpFileOptions:CommentHeader"] = ["None", "Simple", "Full"],
        ["NpgsqlRest:HttpFileOptions:FileMode"] = ["Database", "Schema"],
        ["NpgsqlRest:ClientCodeGen:CommentHeader"] = ["None", "Simple", "Full"],
        ["NpgsqlRest:CrudSource:CommentsMode"] = ["Ignore", "ParseAll", "OnlyWithHttpTag"],

        // ConnectionSettings
        ["ConnectionSettings:MultiHostConnectionTargets:Default"] = ["Any", "Primary", "Standby", "PreferPrimary", "PreferStandby", "ReadWrite", "ReadOnly"],

        // Stats
        ["Stats:OutputFormat"] = ["json", "html", "tsv"],

        // RateLimiterOptions policies
        ["RateLimiterOptions:Policies:Type"] = ["FixedWindow", "SlidingWindow", "TokenBucket", "Concurrency"],

        // ValidationOptions rules
        ["ValidationOptions:Rules:Type"] = ["NotNull", "NotEmpty", "Required", "Regex", "MinLength", "MaxLength", "Range"],
    };

    public static JsonObject Generate()
    {
        var defaults = ConfigDefaults.GetDefaults();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft-07/schema#",
            ["title"] = "NpgsqlRest Configuration",
            ["description"] = "Configuration schema for NpgsqlRest appsettings.json",
            ["type"] = "object",
            ["properties"] = GenerateProperties(defaults, "")
        };
        return schema;
    }

    private static JsonObject GenerateProperties(JsonObject obj, string parentPath)
    {
        var properties = new JsonObject();
        foreach (var kvp in obj)
        {
            var path = string.IsNullOrEmpty(parentPath) ? kvp.Key : $"{parentPath}:{kvp.Key}";
            properties[kvp.Key] = GeneratePropertySchema(kvp.Value, path);
        }
        return properties;
    }

    private static JsonNode GeneratePropertySchema(JsonNode? value, string path)
    {
        if (value is null)
        {
            // Null default — check if it's a known enum
            if (EnumFields.TryGetValue(path, out var enumValues))
            {
                var schema = new JsonObject();
                var enumArray = new JsonArray();
                enumArray.Add((JsonNode?)null);
                foreach (var v in enumValues) enumArray.Add((JsonNode)v);
                schema["enum"] = enumArray;
                schema["default"] = null;
                return schema;
            }
            // Nullable string by default
            return new JsonObject
            {
                ["type"] = new JsonArray { (JsonNode)"string", (JsonNode)"null" },
                ["default"] = null
            };
        }

        if (value is JsonObject childObj)
        {
            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = GenerateProperties(childObj, path)
            };
            return schema;
        }

        if (value is JsonArray childArr)
        {
            var schema = new JsonObject
            {
                ["type"] = "array"
            };

            if (childArr.Count > 0)
            {
                var firstItem = childArr[0];
                if (firstItem is JsonObject itemObj)
                {
                    schema["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = GenerateProperties(itemObj, path)
                    };
                }
                else
                {
                    schema["items"] = InferValueSchema(firstItem, path);
                }
            }

            // Include default array values
            var defaultArr = new JsonArray();
            foreach (var item in childArr)
            {
                defaultArr.Add(item?.DeepClone());
            }
            schema["default"] = defaultArr;
            return schema;
        }

        // Primitive value
        return InferValueSchema(value, path);
    }

    private static JsonObject InferValueSchema(JsonNode? value, string path)
    {
        var schema = new JsonObject();

        // Check for known enum
        if (EnumFields.TryGetValue(path, out var enumValues))
        {
            var enumArray = new JsonArray();
            foreach (var v in enumValues) enumArray.Add((JsonNode)v);
            schema["enum"] = enumArray;
            if (value is not null) schema["default"] = value.DeepClone();
            return schema;
        }

        if (value is null)
        {
            schema["type"] = new JsonArray { (JsonNode)"string", (JsonNode)"null" };
            return schema;
        }

        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var boolVal))
            {
                schema["type"] = "boolean";
                schema["default"] = boolVal;
            }
            else if (jv.TryGetValue<int>(out var intVal))
            {
                schema["type"] = "integer";
                schema["default"] = intVal;
            }
            else if (jv.TryGetValue<long>(out var longVal))
            {
                schema["type"] = "integer";
                schema["default"] = longVal;
            }
            else if (jv.TryGetValue<double>(out var doubleVal))
            {
                schema["type"] = "number";
                schema["default"] = doubleVal;
            }
            else if (jv.TryGetValue<string>(out var strVal))
            {
                schema["type"] = "string";
                schema["default"] = strVal;
            }
            else
            {
                schema["type"] = "string";
                schema["default"] = value.DeepClone();
            }
        }

        return schema;
    }
}
