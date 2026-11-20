namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    /// <summary>
    /// The annotations core reads from endpoint comments, described as data. Part of the reference
    /// (<see cref="CommentAnnotations"/>, <c>npgsqlrest --annotations</c>); plugins and handlers add their own.
    /// </summary>
    internal static CommentAnnotation[] CoreAnnotations() =>
        [
            new CommentAnnotation
            {
                Name = "http",
                Aliases = [HttpKey],
                Syntax = "http [GET|POST|PUT|DELETE|QUERY] [path]",
                Description = "Enable endpoint and configure HTTP method and/or path. Required (for HTTP exposure) when CommentsMode is OnlyAnnotated (or its alias OnlyWithHttpTag).",
                ValueKind = AnnotationValueKind.Text,
                Example = "HTTP GET /users/{id}",
            },
            new CommentAnnotation
            {
                Name = "path",
                Aliases = [PathKey],
                Syntax = "path <url_path>",
                Description = "Override the endpoint URL path.",
                ValueKind = AnnotationValueKind.Path,
                Example = "path /users/{id}",
            },
            new CommentAnnotation
            {
                Name = "param_type",
                Aliases = ParamTypeKey,
                Syntax = "param_type [query_string|query|body_json|body]",
                Description = "Set request parameter type to query string or JSON body.",
                ValueKind = AnnotationValueKind.Enum,
                Values = ["query_string", "query", "body_json", "body"],
                Example = "param_type body_json",
            },
            new CommentAnnotation
            {
                Name = "authorize",
                Aliases = AuthorizeKey,
                Syntax = "authorize [role1, role2, user:name, id:user_id, ...]",
                Description = "Require authorization, optionally restricting access. Plain values are role names (role claim); user:name matches the user name claim and id:value the user id claim.",
                ValueKind = AnnotationValueKind.List,
                Example = "authorize admin, user:alice",
            },
            new CommentAnnotation
            {
                Name = "allow_anonymous",
                Aliases = AllowAnonymousKey,
                Syntax = "allow_anonymous",
                Description = "Allow unauthenticated access to this endpoint.",
                ValueKind = AnnotationValueKind.None,
                Example = "allow_anonymous",
            },
            new CommentAnnotation
            {
                Name = "login",
                Aliases = LoginKey,
                Syntax = "login",
                Description = "Mark endpoint as a login/authentication endpoint.",
                ValueKind = AnnotationValueKind.None,
                Example = "login",
            },
            new CommentAnnotation
            {
                Name = "logout",
                Aliases = LogoutKey,
                Syntax = "logout",
                Description = "Mark endpoint as a logout endpoint.",
                ValueKind = AnnotationValueKind.None,
                Example = "logout",
            },
            new CommentAnnotation
            {
                Name = "raw",
                Aliases = RawKey,
                Syntax = "raw",
                Description = "Return raw results without JSON formatting.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "raw",
            },
            new CommentAnnotation
            {
                Name = "separator",
                Aliases = SeparatorKey,
                Syntax = "separator <value>",
                Description = "Set the value separator for raw mode output.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "separator ,",
            },
            new CommentAnnotation
            {
                Name = "new_line",
                Aliases = NewLineKey,
                Syntax = "new_line <value>",
                Description = "Set the line separator for raw mode output.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "new_line \\n",
            },
            new CommentAnnotation
            {
                Name = "columns",
                Aliases = ColumnNamesKey,
                Syntax = "columns",
                Description = "Include column names as the first row in raw output.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "columns",
            },
            new CommentAnnotation
            {
                Name = "buffer_rows",
                Aliases = BufferRowsKey,
                Syntax = "buffer_rows <number>",
                Description = "Set the number of rows to buffer before sending response.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Integer,
                Default = "25",
                Example = "buffer_rows 100",
            },
            new CommentAnnotation
            {
                Name = "cached",
                Aliases = [CacheKey],
                Syntax = "cached [param1, param2, ...]",
                Description = "Enable response caching, optionally specifying cache key parameters.",
                ValueKind = AnnotationValueKind.List,
                Example = "cached _user_id",
            },
            new CommentAnnotation
            {
                Name = "cache_expires",
                Aliases = CacheExpiresInKey,
                Syntax = "cache_expires <interval>",
                Description = "Set cache expiration time (PostgreSQL interval format).",
                ValueKind = AnnotationValueKind.Interval,
                Example = "cache_expires 5 minutes",
            },
            new CommentAnnotation
            {
                Name = "cache_profile",
                Aliases = [CacheProfileKey],
                Syntax = "cache_profile <name>",
                Description = "Select a named cache profile defined in CacheOptions.Profiles. The profile supplies the cache backend, default expiration, default key parameters, and per-parameter skip conditions. Implies caching even without @cached. Existing @cached and @cache_expires annotations override the profile's defaults. Unknown profile names cause startup to fail.",
                ValueKind = AnnotationValueKind.Name,
                Example = "cache_profile fast_memory",
            },
            new CommentAnnotation
            {
                Name = "connection_name",
                Aliases = ConnectionNameKey,
                Syntax = "connection_name <name>",
                Description = "Use a specific named connection string for this endpoint.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Name,
                Example = "connection read_replica",
            },
            new CommentAnnotation
            {
                Name = "timeout",
                Aliases = TimeoutKey,
                Syntax = "timeout <interval>",
                Description = "Set command execution timeout (PostgreSQL interval format).",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Interval,
                Example = "timeout 30s",
            },
            new CommentAnnotation
            {
                Name = "request_headers_mode",
                Aliases = RequestHeadersModeKey,
                Syntax = "request_headers [ignore|context|parameter]",
                Description = "Control how HTTP request headers are passed to the routine.",
                ValueKind = AnnotationValueKind.Enum,
                Values = ["ignore", "context", "parameter"],
                Example = "request_headers context",
            },
            new CommentAnnotation
            {
                Name = "request_headers_parameter_name",
                Aliases = RequestHeadersParameterNameKey,
                Syntax = "request_headers_parameter_name <name>",
                Description = "Set the parameter name for request headers when mode is parameter.",
                ValueKind = AnnotationValueKind.Name,
                Example = "request_headers_parameter_name _headers",
            },
            new CommentAnnotation
            {
                Name = "body_parameter_name",
                Aliases = BodyParameterNameKey,
                Syntax = "body_parameter_name <name>",
                Description = "Set the parameter name for the JSON body content.",
                ValueKind = AnnotationValueKind.Name,
                Example = "body_parameter_name _body",
            },
            new CommentAnnotation
            {
                Name = "response_null_handling",
                Aliases = TextResponseNullHandlingKey,
                Syntax = "response_null [empty_string|null_literal|no_content|204]",
                Description = "Control how NULL return values are rendered in responses.",
                ValueKind = AnnotationValueKind.Enum,
                Values = ["empty_string", "null_literal", "no_content", "204"],
                Default = "empty_string",
                Example = "response_null no_content",
            },
            new CommentAnnotation
            {
                Name = "query_string_null_handling",
                Aliases = QueryStringNullHandlingKey,
                Syntax = "query_null [empty_string|null_literal|ignore]",
                Description = "Control how NULL query string parameters are handled.",
                ValueKind = AnnotationValueKind.Enum,
                Values = ["empty_string", "null_literal", "ignore"],
                Default = "ignore",
                Example = "query_null empty_string",
            },
            new CommentAnnotation
            {
                Name = "security_sensitive",
                Aliases = SecuritySensitiveKey,
                Syntax = "security_sensitive",
                Description = "Mark endpoint as security-sensitive (suppresses logging of parameters).",
                ValueKind = AnnotationValueKind.None,
                Example = "security_sensitive",
            },
            new CommentAnnotation
            {
                Name = "user_context",
                Aliases = UserContextKey,
                Syntax = "user_context",
                Description = "Pass authenticated user context to the routine via connection settings.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "user_context",
            },
            new CommentAnnotation
            {
                Name = "user_parameters",
                Aliases = UserParemetersKey,
                Syntax = "user_parameters",
                Description = "Map user claims to routine parameters.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "user_parameters",
            },
            new CommentAnnotation
            {
                Name = "upload",
                Aliases = [UploadKey],
                Syntax = "upload [for handler1, handler2, ...]",
                Description = "Enable file upload for this endpoint, optionally specifying upload handlers.",
                ValueKind = AnnotationValueKind.List,
                Example = "upload for file_system",
            },
            new CommentAnnotation
            {
                Name = "param",
                Aliases = ParameterKey,
                Syntax = "param <name> is hash of <other_name> | param <name> is upload metadata | param <original> <new_name> [type] | param <original> is <new_name> [type] | param <name> type is <type> | param <name> default <value>",
                Description = "Configure parameter behavior: hash computation, upload metadata binding, rename/retype, retype WITHOUT rename ('param user_id type is integer'), or default value ('param user_id default null'). Rename forms: 'param $1 user_id', 'param $1 user_id integer', 'param $1 is user_id', 'param _old_name better_name'. In SQL files with named (:name) placeholders the parameter is addressed by the placeholder's own name (a leading ':' is tolerated). Works on all endpoint types.",
                ValueKind = AnnotationValueKind.Text,
                Example = "param $1 user_id integer",
            },
            new CommentAnnotation
            {
                Name = "sse_path",
                Aliases = SseEventsStreamingPathKey,
                Syntax = "sse_path [path] [on info|notice|warning]",
                Description = "Enable Server-Sent Events streaming with optional path and notice level.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Path,
                Example = "sse /events on notice",
            },
            new CommentAnnotation
            {
                Name = "sse_level",
                Aliases = SseEventsLevelKey,
                Syntax = "sse_level [info|notice|warning]",
                Description = "Set the PostgreSQL notice level for SSE events.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Enum,
                Values = ["info", "notice", "warning"],
                Example = "sse_level notice",
            },
            new CommentAnnotation
            {
                Name = "sse_scope",
                Aliases = SseEventsStreamingScopeKey,
                Syntax = "sse_scope [all|authorize|matching] [role1, role2, user:name, id:user_id, ...]",
                Description = "Set the broadcast scope for SSE events. Authorize values: plain = role, user:name = user name claim, id:value = user id claim.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "sse_scope authorize admin",
            },
            new CommentAnnotation
            {
                Name = "basic_auth",
                Aliases = BasicAuthKey,
                Syntax = "basic_auth [username] [password]",
                Description = "Enable HTTP Basic Authentication for this endpoint.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "basic_auth",
            },
            new CommentAnnotation
            {
                Name = "basic_auth_realm",
                Aliases = BasicAuthRealmKey,
                Syntax = "basic_auth_realm <realm>",
                Description = "Set the authentication realm for Basic Auth challenges.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "basic_auth_realm Reports",
            },
            new CommentAnnotation
            {
                Name = "basic_auth_command",
                Aliases = BasicAuthCommandKey,
                Syntax = "basic_auth_command <sql_command>",
                Description = "Set a custom SQL command for Basic Auth credential validation.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Sql,
                Example = "basic_auth_command = select * from check_basic_auth($1, $2)",
            },
            new CommentAnnotation
            {
                Name = "retry_strategy",
                Aliases = RetryStrategyKey,
                Syntax = "retry_strategy <name>",
                Description = "Apply a named retry strategy for transient database errors.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Name,
                Example = "retry_strategy default",
            },
            new CommentAnnotation
            {
                Name = "rate_limiter",
                Aliases = RateLimiterPolicyKey,
                Syntax = "rate_limiter <policy_name>",
                Description = "Apply a named rate limiting policy to this endpoint.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Name,
                Example = "rate_limiter fixed",
            },
            new CommentAnnotation
            {
                Name = "error_code_policy",
                Aliases = ErrorCodePolicyKey,
                Syntax = "error_code_policy <name>",
                Description = "Apply a named error code mapping policy for PostgreSQL error codes.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Name,
                Example = "error_code_policy default",
            },
            new CommentAnnotation
            {
                Name = "validate",
                Aliases = ValidateKey,
                Syntax = "validate <param_name> using <rule_name>",
                Description = "Add parameter validation using a named validation rule.",
                ValueKind = AnnotationValueKind.Text,
                Example = "validate _email using email",
            },
            new CommentAnnotation
            {
                Name = "proxy",
                Aliases = ProxyKey,
                Syntax = "proxy [GET|POST|PUT|DELETE|QUERY] [host_url]",
                Description = "Configure endpoint as a reverse proxy.",
                ValueKind = AnnotationValueKind.Text,
                Example = "proxy GET https://api.example.com/users",
            },
            new CommentAnnotation
            {
                Name = "proxy_out",
                Aliases = ProxyOutKey,
                Syntax = "proxy_out [GET|POST|PUT|DELETE|QUERY] [host_url]",
                Description = "Execute function first, then forward result body to upstream proxy service.",
                ValueKind = AnnotationValueKind.Text,
                Example = "proxy_out POST https://api.example.com/render",
            },
            new CommentAnnotation
            {
                Name = "nested_json",
                Aliases = NestedJsonKey,
                Syntax = "nested_json",
                Description = "Serialize composite type columns as nested JSON objects.",
                ValueKind = AnnotationValueKind.None,
                Example = "nested_json",
            },
            new CommentAnnotation
            {
                Name = "tags",
                Aliases = TagsKey,
                Syntax = "for tag1, tag2, ...",
                Description = "Filter endpoint availability by tags.",
                Contexts = AnnotationContext.Routine,
                ValueKind = AnnotationValueKind.List,
                Example = "for function, stable",
            },
            new CommentAnnotation
            {
                Name = "disabled",
                Aliases = [DisabledKey],
                Syntax = "disabled [tag1, tag2, ...]",
                Description = "Disable this endpoint, optionally only for specific tags.",
                ValueKind = AnnotationValueKind.List,
                Example = "disabled",
            },
            new CommentAnnotation
            {
                Name = "enabled",
                Aliases = [EnabledKey],
                Syntax = "enabled [tag1, tag2, ...]",
                Description = "Enable this endpoint, optionally only for specific tags.",
                ValueKind = AnnotationValueKind.List,
                Example = "enabled",
            },
            new CommentAnnotation
            {
                Name = "internal",
                Aliases = InternalKey,
                Syntax = "internal",
                Description = "Mark endpoint as internal-only. Not exposed as an HTTP route, only accessible via self-referencing calls (proxy, HTTP client types).",
                ValueKind = AnnotationValueKind.None,
                Example = "internal",
            },
            new CommentAnnotation
            {
                Name = "encrypt",
                Aliases = EncryptKey,
                Syntax = "encrypt [param1, param2, ...]",
                Description = "Encrypt parameter values using the default data protector before sending to PostgreSQL. Without arguments, encrypts all text parameters.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.List,
                Example = "encrypt _card_number",
            },
            new CommentAnnotation
            {
                Name = "decrypt",
                Aliases = DecryptKey,
                Syntax = "decrypt [column1, column2, ...]",
                Description = "Decrypt result column values using the default data protector before returning to the client. Without arguments, decrypts all text columns.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.List,
                Example = "decrypt card_number",
            },
            new CommentAnnotation
            {
                Name = "custom_parameter",
                Aliases = [],
                Syntax = "key = value",
                Description = "Define a custom parameter as key-value pair (separated by =).",
                Form = AnnotationForm.KeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "my_key = my value",
            },
            new CommentAnnotation
            {
                Name = "header",
                Aliases = [],
                Syntax = "Header-Name: header-value",
                Description = "Add a response header (separated by :).",
                Form = AnnotationForm.Line,
                ValueKind = AnnotationValueKind.Text,
                Example = "Content-Type: text/csv",
            },
            new CommentAnnotation
            {
                Name = "result",
                Aliases = ["result"],
                Syntax = "result <name> | result is <name>",
                Description = "Name a statement's result in the response of a SQL file with several statements (the default names are result1, result2 ...). Positional: applies to the next statement, or, written after the semicolon on the same line, to the statement that line ends.",
                Contexts = AnnotationContext.SqlFileStatement,
                ValueKind = AnnotationValueKind.Name,
                Example = "result users",
            },
            new CommentAnnotation
            {
                Name = "define_param",
                Aliases = ["define_param"],
                Syntax = "define_param <name> [type]",
                Description = "Define a virtual parameter that exists for HTTP matching and claim mapping but is NOT bound to the PostgreSQL command. Useful for SQL file endpoints where you need parameters for comment placeholders or claim mapping without referencing them in SQL. Default type is text. SQL file source only.",
                Contexts = AnnotationContext.SqlFile,
                ValueKind = AnnotationValueKind.Text,
                Example = "define_param tenant_id integer",
            },
            new CommentAnnotation
            {
                Name = "single",
                Aliases = SingleKey,
                Syntax = "single",
                Description = "Return only the first row as a JSON object instead of a JSON array. In multi-command SQL files the annotation is positional: it applies to the next statement (or, inline after the semicolon, to the statement on that line).",
                Form = AnnotationForm.KeywordOrKeyValue,
                Contexts = AnnotationContext.Endpoint | AnnotationContext.SqlFileStatement,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "single",
            },
            new CommentAnnotation
            {
                Name = "void",
                Aliases = VoidKey,
                Syntax = "void",
                Description = "Return no content (204 No Content); results are discarded. In multi-command SQL files all statements still execute for side effects.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Boolean,
                Default = "false",
                Example = "void",
            },
            new CommentAnnotation
            {
                Name = "skip",
                Aliases = ["skip", "skip_result", "no_result"],
                Syntax = "skip",
                Description = "Execute the statement but exclude its result from the response. Positional: applies to the next statement (or, inline after the semicolon, to the statement on that line). SQL file source only.",
                Contexts = AnnotationContext.SqlFileStatement,
                ValueKind = AnnotationValueKind.None,
                Example = "skip",
            },
            new CommentAnnotation
            {
                Name = "returns",
                Aliases = ["returns"],
                Syntax = "returns <composite_type> | returns <scalar_type> | returns void",
                Description = "Skip the startup Describe for a statement and resolve result columns from the given type instead: a composite type name (schema-qualified or not), a built-in scalar type (single-column result), or void. Needed when a statement references objects that do not exist at startup (e.g. a temp table created in a DO block). SQL file source only.",
                Contexts = AnnotationContext.SqlFileStatement,
                ValueKind = AnnotationValueKind.Name,
                Example = "returns my_schema.user_row",
            },
            new CommentAnnotation
            {
                Name = "sse_publish",
                Aliases = SseEventsPublishKey,
                Syntax = "sse_publish [on info|notice|warning]",
                Description = "Publish this routine's RAISE messages of the given level (default: the configured level) as Server-Sent Events, without giving the endpoint an event stream of its own. Subscribers are endpoints with sse or sse_subscribe.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Text,
                Example = "sse_publish on notice",
            },
            new CommentAnnotation
            {
                Name = "sse_subscribe",
                Aliases = SseEventsSubscribeKey,
                Syntax = "sse_subscribe [path] [on info|notice|warning]",
                Description = "Give this endpoint a Server-Sent Events stream that clients subscribe to, without publishing this routine's own RAISE messages. Without a path, the path is the level's name.",
                Form = AnnotationForm.KeywordOrKeyValue,
                ValueKind = AnnotationValueKind.Path,
                Example = "sse_subscribe /events",
            },
            new CommentAnnotation
            {
                Name = "table_format",
                Aliases = ["table_format"],
                Syntax = "table_format = <format>",
                Description = "Return the rows in a table format instead of JSON: a key of the configured table format handlers (the npgsqlrest client has html and excel when TableFormatOptions is enabled).",
                Form = AnnotationForm.KeyValue,
                ValueKind = AnnotationValueKind.Name,
                Example = "table_format = html",
            },
            .. UploadHandlers.UploadAnnotations.Core,
            .. HttpClientType.HttpTypeAnnotations.All,
        ];
}
