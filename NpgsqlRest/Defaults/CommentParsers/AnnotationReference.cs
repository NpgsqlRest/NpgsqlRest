using System.Text.Json.Nodes;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// Returns a machine-readable JSON array describing all supported comment annotations.
    /// Used by pgdev and other tools for autocomplete/IntelliSense.
    /// </summary>
    public static JsonArray GetAnnotationReference()
    {
        var annotations = new JsonArray();

        static JsonArray ToJsonArray(string[] values)
        {
            var arr = new JsonArray();
            foreach (var v in values) arr.Add((JsonNode)v);
            return arr;
        }

        static JsonArray ToJsonArray1(string value) => new() { (JsonNode)value };

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "http",
            ["aliases"] = ToJsonArray1(HttpKey),
            ["syntax"] = "http [GET|POST|PUT|DELETE] [path]",
            ["description"] = "Enable endpoint and configure HTTP method and/or path. Required when CommentsMode is OnlyWithHttpTag."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "path",
            ["aliases"] = ToJsonArray1(PathKey),
            ["syntax"] = "path <url_path>",
            ["description"] = "Override the endpoint URL path."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "param_type",
            ["aliases"] = ToJsonArray(ParamTypeKey),
            ["syntax"] = "param_type [query_string|query|body_json|body]",
            ["description"] = "Set request parameter type to query string or JSON body."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "authorize",
            ["aliases"] = ToJsonArray(AuthorizeKey),
            ["syntax"] = "authorize [role1, role2, ...]",
            ["description"] = "Require authorization, optionally restricting to specific roles."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "allow_anonymous",
            ["aliases"] = ToJsonArray(AllowAnonymousKey),
            ["syntax"] = "allow_anonymous",
            ["description"] = "Allow unauthenticated access to this endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "login",
            ["aliases"] = ToJsonArray(LoginKey),
            ["syntax"] = "login",
            ["description"] = "Mark endpoint as a login/authentication endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "logout",
            ["aliases"] = ToJsonArray(LogoutKey),
            ["syntax"] = "logout",
            ["description"] = "Mark endpoint as a logout endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "raw",
            ["aliases"] = ToJsonArray(RawKey),
            ["syntax"] = "raw",
            ["description"] = "Return raw results without JSON formatting."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "separator",
            ["aliases"] = ToJsonArray(SeparatorKey),
            ["syntax"] = "separator <value>",
            ["description"] = "Set the value separator for raw mode output."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "new_line",
            ["aliases"] = ToJsonArray(NewLineKey),
            ["syntax"] = "new_line <value>",
            ["description"] = "Set the line separator for raw mode output."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "columns",
            ["aliases"] = ToJsonArray(ColumnNamesKey),
            ["syntax"] = "columns",
            ["description"] = "Include column names as the first row in raw output."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "buffer_rows",
            ["aliases"] = ToJsonArray(BufferRowsKey),
            ["syntax"] = "buffer_rows <number>",
            ["description"] = "Set the number of rows to buffer before sending response."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "cached",
            ["aliases"] = ToJsonArray1(CacheKey),
            ["syntax"] = "cached [param1, param2, ...]",
            ["description"] = "Enable response caching, optionally specifying cache key parameters."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "cache_expires",
            ["aliases"] = ToJsonArray(CacheExpiresInKey),
            ["syntax"] = "cache_expires <interval>",
            ["description"] = "Set cache expiration time (PostgreSQL interval format)."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "connection_name",
            ["aliases"] = ToJsonArray(ConnectionNameKey),
            ["syntax"] = "connection_name <name>",
            ["description"] = "Use a specific named connection string for this endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "timeout",
            ["aliases"] = ToJsonArray(TimeoutKey),
            ["syntax"] = "timeout <interval>",
            ["description"] = "Set command execution timeout (PostgreSQL interval format)."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "request_headers_mode",
            ["aliases"] = ToJsonArray(RequestHeadersModeKey),
            ["syntax"] = "request_headers [ignore|context|parameter]",
            ["description"] = "Control how HTTP request headers are passed to the routine."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "request_headers_parameter_name",
            ["aliases"] = ToJsonArray(RequestHeadersParameterNameKey),
            ["syntax"] = "request_headers_parameter_name <name>",
            ["description"] = "Set the parameter name for request headers when mode is parameter."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "body_parameter_name",
            ["aliases"] = ToJsonArray(BodyParameterNameKey),
            ["syntax"] = "body_parameter_name <name>",
            ["description"] = "Set the parameter name for the JSON body content."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "response_null_handling",
            ["aliases"] = ToJsonArray(TextResponseNullHandlingKey),
            ["syntax"] = "response_null [empty_string|null_literal|no_content|204]",
            ["description"] = "Control how NULL return values are rendered in responses."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "query_string_null_handling",
            ["aliases"] = ToJsonArray(QueryStringNullHandlingKey),
            ["syntax"] = "query_null [empty_string|null_literal|ignore]",
            ["description"] = "Control how NULL query string parameters are handled."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "security_sensitive",
            ["aliases"] = ToJsonArray(SecuritySensitiveKey),
            ["syntax"] = "security_sensitive",
            ["description"] = "Mark endpoint as security-sensitive (suppresses logging of parameters)."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "user_context",
            ["aliases"] = ToJsonArray(UserContextKey),
            ["syntax"] = "user_context",
            ["description"] = "Pass authenticated user context to the routine via connection settings."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "user_parameters",
            ["aliases"] = ToJsonArray(UserParemetersKey),
            ["syntax"] = "user_parameters",
            ["description"] = "Map user claims to routine parameters."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "upload",
            ["aliases"] = ToJsonArray1(UploadKey),
            ["syntax"] = "upload [for handler1, handler2, ...]",
            ["description"] = "Enable file upload for this endpoint, optionally specifying upload handlers."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "param",
            ["aliases"] = ToJsonArray(ParameterKey),
            ["syntax"] = "param <name> is hash of <other_name> | param <name> is upload metadata",
            ["description"] = "Configure parameter behavior: hash computation or upload metadata binding."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "sse_path",
            ["aliases"] = ToJsonArray(SseEventsStreamingPathKey),
            ["syntax"] = "sse_path [path] [on info|notice|warning]",
            ["description"] = "Enable Server-Sent Events streaming with optional path and notice level."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "sse_level",
            ["aliases"] = ToJsonArray(SseEventsLevelKey),
            ["syntax"] = "sse_level [info|notice|warning]",
            ["description"] = "Set the PostgreSQL notice level for SSE events."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "sse_scope",
            ["aliases"] = ToJsonArray(SseEventsStreamingScopeKey),
            ["syntax"] = "sse_scope [all|authorize|matching] [role1, role2, ...]",
            ["description"] = "Set the broadcast scope for SSE events."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "basic_auth",
            ["aliases"] = ToJsonArray(BasicAuthKey),
            ["syntax"] = "basic_auth [username] [password]",
            ["description"] = "Enable HTTP Basic Authentication for this endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "basic_auth_realm",
            ["aliases"] = ToJsonArray(BasicAuthRealmKey),
            ["syntax"] = "basic_auth_realm <realm>",
            ["description"] = "Set the authentication realm for Basic Auth challenges."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "basic_auth_command",
            ["aliases"] = ToJsonArray(BasicAuthCommandKey),
            ["syntax"] = "basic_auth_command <sql_command>",
            ["description"] = "Set a custom SQL command for Basic Auth credential validation."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "retry_strategy",
            ["aliases"] = ToJsonArray(RetryStrategyKey),
            ["syntax"] = "retry_strategy <name>",
            ["description"] = "Apply a named retry strategy for transient database errors."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "rate_limiter",
            ["aliases"] = ToJsonArray(RateLimiterPolicyKey),
            ["syntax"] = "rate_limiter <policy_name>",
            ["description"] = "Apply a named rate limiting policy to this endpoint."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "error_code_policy",
            ["aliases"] = ToJsonArray(ErrorCodePolicyKey),
            ["syntax"] = "error_code_policy <name>",
            ["description"] = "Apply a named error code mapping policy for PostgreSQL error codes."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "validate",
            ["aliases"] = ToJsonArray(ValidateKey),
            ["syntax"] = "validate <param_name> using <rule_name>",
            ["description"] = "Add parameter validation using a named validation rule."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "proxy",
            ["aliases"] = ToJsonArray(ProxyKey),
            ["syntax"] = "proxy [GET|POST|PUT|DELETE|PATCH] [host_url]",
            ["description"] = "Configure endpoint as a reverse proxy."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "nested_json",
            ["aliases"] = ToJsonArray(NestedJsonKey),
            ["syntax"] = "nested_json",
            ["description"] = "Serialize composite type columns as nested JSON objects."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "tags",
            ["aliases"] = ToJsonArray(TagsKey),
            ["syntax"] = "for tag1, tag2, ...",
            ["description"] = "Filter endpoint availability by tags."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "disabled",
            ["aliases"] = ToJsonArray1(DisabledKey),
            ["syntax"] = "disabled [tag1, tag2, ...]",
            ["description"] = "Disable this endpoint, optionally only for specific tags."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "enabled",
            ["aliases"] = ToJsonArray1(EnabledKey),
            ["syntax"] = "enabled [tag1, tag2, ...]",
            ["description"] = "Enable this endpoint, optionally only for specific tags."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "custom_parameter",
            ["aliases"] = new JsonArray(),
            ["syntax"] = "key = value",
            ["description"] = "Define a custom parameter as key-value pair (separated by =)."
        });

        annotations.Add((JsonNode)new JsonObject
        {
            ["name"] = "header",
            ["aliases"] = new JsonArray(),
            ["syntax"] = "Header-Name: header-value",
            ["description"] = "Add a response header (separated by :)."
        });

        return annotations;
    }
}
