using System.Text.RegularExpressions;
using Microsoft.Extensions.Primitives;

namespace NpgsqlRest.Defaults;

internal static class DefaultCommentParser
{
    private static readonly char[] NewlineSeparator = ['\r', '\n'];
    private static readonly char[] WordSeparators = [Consts.Space, Consts.Comma];

    private const string HttpKey = "http";
    private const string PathKey = "path";

    private static readonly string[] ParamTypeKey = [
        "request_param_type",
        "param_type",
    ];
    private static readonly string[] QueryKey = [
        "query_string",
        "query"
    ];
    private static readonly string[] JsonKey = [
        "body_json",
        "body"
    ];
    private static readonly string[] AuthorizeKey = [
        "authorize",
        "authorized",
        "requires_authorization",
    ];
    private static readonly string[] AllowAnonymousKey = [
        "allow_anonymous",
        "anonymous",
        "allow_anon",
        "anon"
    ];
    private static readonly string[] TimeoutKey = [
        "command_timeout",
        "timeout"
    ];
    private static readonly string[] ContentTypeKey = [
        "content-type", // content-type is header key
        "content_type",
    ];

    private static readonly string[] RequestHeadersModeKey = [
        "request_headers_mode",
        "request_headers",
    ];

    private const string RequestHeaderModeIgnoreKey = "ignore";
    private const string RequestHeaderModeContextKey = "context";
    private const string RequestHeaderModeParameterKey = "parameter";

    private static readonly string[] RequestHeadersParameterNameKey = [
        "request_headers_parameter_name",
        "request_headers_param_name",
        "request-headers-param-name",
    ];
    private static readonly string[] BodyParameterNameKey = [
        "body_parameter_name",
        "body_param_name"
    ];
    private static readonly string[] TagsKey = ["for", "tags", "tag"];

    private const string DisabledKey = "disabled";
    private const string EnabledKey = "enabled";

    private static readonly string[] TextResponseNullHandlingKey = [
        "response_null_handling",
        "response_null",
    ];

    private static readonly string[] EmptyStringKey = [
        "empty",
        "empty_string"
    ];

    private static readonly string[] NullLiteral = [
        "null_literal",
        "null"
    ];

    private static readonly string[] NoContentKey = [
        "204",
        "204_no_content",
        "no_content",
    ];

    private static readonly string[] QueryStringNullHandlingKey = [
        "query_string_null_handling",
        "query_null_handling",
        "query_string_null",
        "query_null",
    ];

    private static readonly string[] LoginKey = [
        "login",
        "signin",
    ];

    private static readonly string[] LogoutKey = [
        "logout",
        "signout",
    ];

    private static readonly string[] BufferRowsKey = [
        "buffer_rows",
        "buffer"
    ];

    private static readonly string[] RawKey = [
        "raw",
        "raw_mode",
        "raw_results",
    ];

    private static readonly string[] SeparatorKey = [
        "separator",
        "raw_separator",
    ];

    private static readonly string[] NewLineKey = [
        "new_line",
        "raw_new_line",
    ];

    private static readonly string[] ColumnNamesKey = [
        "columns",
        "names",
        "column_names",
    ];

    private const string CacheKey = "cached";

    private static readonly string[] CacheExpiresInKey = [
        "cache_expires",
        "cache_expires_in",
    ];

    private static readonly string[] ConnectionNameKey = [
        "connection",
        "connection_name",
    ];

    private static readonly string[] SecuritySensitiveKey = [
        "sensitive",
        "security",
        "security_sensitive",
    ];

    private static readonly string[] UserContextKey = [
        "user_context"
    ];

    private static readonly string[] UserParemetersKey = [
        "user_parameters",
        "user_params",
    ];

    private const string UploadKey = "upload";

    private static readonly string[] ParameterKey = [
        "parameter",
        "param",
    ];

    private static readonly string[] InfoEventsStreamingPathKey = [
        "info_path",
        "info_events_path",
        "info_streaming_path"
    ];

    private static readonly string[] InfoEventsStreamingScopeKey = [
        "info_scope",
        "info_events_scope",
        "info_streaming_scope",
    ];
    
    private static readonly string[] BasicAuthKey = [
        "basic_authentication",
        "basic_auth",
    ];
    
    private static readonly string[] BasicAuthRealmKey = [
        "basic_authentication_realm",
        "basic_auth_realm",
        "realm",
    ];
    
    private static readonly string[] BasicAuthCommandKey = [
        "basic_authentication_command",
        "basic_auth_command",
        "challenge_command",
    ];
    
    private static readonly string[] RetryStrategyKey = [
        "retry_strategy_name",
        "retry_strategy",
        "retry",
    ];
    
    private static readonly string[] RateLimiterPolicyKey = [
        "rate_limiter_policy_name",
        "rate_limiter_policy",
        "rate_limiter",
    ];
    
    private static readonly string[] ErrorCodePolicyKey = [
        "error_code_policy_name",
        "error_code_policy",
        "error_code",
    ];

    public static RoutineEndpoint? Parse(
        Routine routine,
        RoutineEndpoint routineEndpoint)
    {
        if (Options.CommentsMode == CommentsMode.Ignore)
        {
            return routineEndpoint;
        }

        var originalUrl = routineEndpoint.Path;
        var originalMethod = routineEndpoint.Method;
        var originalParamType = routineEndpoint.RequestParamType;

        var comment = routine.Comment;
        var disabled = false;
        bool haveTag = true;
        if (string.IsNullOrEmpty(comment))
        {
            if (Options.CommentsMode == CommentsMode.OnlyWithHttpTag)
            {
                return null;
            }
        }
        else
        {
            var routineDescription = string.Concat(routine.Type, " ", routine.Schema, ".", routine.Name);
            var urlDescription = string.Concat(routineEndpoint.Method.ToString(), " ", routineEndpoint.Path);
            var description = string.Concat(routineDescription, " mapped to ", urlDescription);

            string[] lines = comment.Split(NewlineSeparator, StringSplitOptions.RemoveEmptyEntries);
            routineEndpoint.CommentWordLines = new string[lines.Length][];
            bool hasHttpTag = false;
            for (var i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                string[] wordsLower = line.SplitWordsLower();
                string[] words = line.SplitWords();
                
                routineEndpoint.CommentWordLines[i] = wordsLower;
                var len = wordsLower.Length;
                if (len == 0)
                {
                    continue;
                }

                // for tag1, tag2, tag3 [, ...]
                // tag tag1, tag2, tag3 [, ...]
                // tags tag1, tag2, tag3 [, ...]
                if (routine.Tags is not null && routine.Tags.Length > 0 && StrEqualsToArray(wordsLower[0], TagsKey))
                {
                    string[] arr = wordsLower[1..];
                    bool found = false;
                    for (var j = 0; j < routine.Tags.Length; j++)
                    {
                        var tag = routine.Tags[j];
                        if (StrEqualsToArray(tag, arr))
                        {
                            found = true;
                            break;
                        }
                    }
                    haveTag = found;
                }

                // key = value
                // custom_parameter_1 = custom parameter 1 value
                // custom_parameter_2 = custom parameter 2 value
                else if (haveTag is true && SplitBySeparatorChar(line, Consts.Equal, out var customParamName, out var customParamValue))
                {
                    if (customParamValue.Contains(Consts.OpenBrace) && customParamValue.Contains(Consts.CloseBrace))
                    {
                        routineEndpoint.CustomParamsNeedParsing = true;
                    }
                    SetCustomParameter(routineEndpoint, customParamName, customParamValue);
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentSetCustomParemeter(description, customParamName, customParamValue);
                    }
                }

                // key: value
                // Content-Type: application/json
                else if (haveTag is true && SplitBySeparatorChar(line, Consts.Colon, out var headerName, out var headerValue))
                {
                    if (headerValue.Contains(Consts.OpenBrace) && headerValue.Contains(Consts.CloseBrace))
                    {
                        routineEndpoint.HeadersNeedParsing = true;
                    }
                    if (StrEqualsToArray(headerName, ContentTypeKey))
                    {
                        if (!string.Equals(routineEndpoint.ResponseContentType, headerValue))
                        {
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentSetContentType(description, headerValue);
                            }
                        }
                        routineEndpoint.ResponseContentType = headerValue;
                    }
                    else
                    {
                        if (routineEndpoint.ResponseHeaders is null)
                        {
                            routineEndpoint.ResponseHeaders = new()
                            {
                                [headerName] = new StringValues(headerValue)
                            };
                        }
                        else
                        {
                            if (routineEndpoint.ResponseHeaders.TryGetValue(headerName, out StringValues values))
                            {
                                routineEndpoint.ResponseHeaders[headerName] = StringValues.Concat(values, headerValue);
                            }
                            else
                            {
                                routineEndpoint.ResponseHeaders.Add(headerName, new StringValues(headerValue));
                            }
                        }
                        if (!string.Equals(routineEndpoint.ResponseContentType, headerValue))
                        {
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentSetHeader(description, headerName, headerValue);
                            }
                        }
                    }
                }

                // disabled
                // disabled tag1, tag2, tag3 [, ...]
                else if (haveTag is true && StrEquals(wordsLower[0], DisabledKey))
                {
                    if (len == 1)
                    {
                        disabled = true;
                    }
                    else if (routine.Tags is not null && routine.Tags.Length > 0)
                    {
                        string[] arr = wordsLower[1..];
                        for (var j = 0; j < routine.Tags.Length; j++)
                        {
                            var tag = routine.Tags[j];
                            if (StrEqualsToArray(tag, arr))
                            {
                                disabled = true;
                                break;
                            }
                        }
                    }
                }

                // enabled
                // enabled [ tag1, tag2, tag3 [, ...] ]
                else if (haveTag is true && StrEquals(wordsLower[0], EnabledKey))
                {
                    if (len == 1)
                    {
                        disabled = false;
                    }
                    else if (routine.Tags is not null && routine.Tags.Length > 0)
                    {
                        string[] arr = wordsLower[1..];
                        for (var j = 0; j < routine.Tags.Length; j++)
                        {
                            var tag = routine.Tags[j];
                            if (StrEqualsToArray(tag, arr))
                            {
                                disabled = false;
                                break;
                            }
                        }
                    }
                }

                // HTTP 
                // HTTP [ GET | POST | PUT | DELETE ]
                // HTTP [ GET | POST | PUT | DELETE ] path
                // HTTP path
                else if (haveTag is true && StrEquals(wordsLower[0], HttpKey))
                {
                    hasHttpTag = true;
                    string? urlPathSegment = null;
                    if (len == 2 || len == 3)
                    {
                        if (Enum.TryParse<Method>(wordsLower[1], true, out var parsedMethod))
                        {
                            routineEndpoint.Method = parsedMethod;
                            routineEndpoint.RequestParamType = routineEndpoint.Method == Method.GET ? RequestParamType.QueryString : RequestParamType.BodyJson;
                        }
                        else
                        {
                            urlPathSegment = wordsLower[1];
                            //Logger?.InvalidHttpMethodComment(words[1], description, routineEndpoint.Method);
                        }
                    }
                    if (len == 3)
                    {
                        urlPathSegment = wordsLower[2];
                    }
                    if (urlPathSegment is not null)
                    {
                        if (!Uri.TryCreate(urlPathSegment, UriKind.Relative, out Uri? uri))
                        {
                            Logger?.InvalidUrlPathSegmentComment(urlPathSegment, description, routineEndpoint.Path);
                        }
                        else
                        {
                            routineEndpoint.Path = uri.ToString();
                            if (!routineEndpoint.Path.StartsWith('/'))
                            {
                                routineEndpoint.Path = string.Concat("/", routineEndpoint.Path);
                            }
                        }
                    }
                    if (routineEndpoint.Method != originalMethod || !string.Equals(routineEndpoint.Path, originalUrl))
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetHttp(description, routineEndpoint.Method, routineEndpoint.Path);
                        }
                        urlDescription = string.Concat(routineEndpoint.Method.ToString(), " ", routineEndpoint.Path);
                        description = string.Concat(routineDescription, " mapped to ", urlDescription);
                    }
                }

                // PATH path
                else if (haveTag is true && StrEquals(wordsLower[0], PathKey))
                {
                    if (len == 2)
                    {
                        string? urlPathSegment = wordsLower[1];
                        if (!Uri.TryCreate(urlPathSegment, UriKind.Relative, out Uri? uri))
                        {
                            Logger?.InvalidUrlPathSegmentComment(urlPathSegment, description, routineEndpoint.Path);
                        }
                        else
                        {
                            routineEndpoint.Path = uri.ToString();
                            if (!routineEndpoint.Path.StartsWith('/'))
                            {
                                routineEndpoint.Path = string.Concat("/", routineEndpoint.Path);
                            }

                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentSetHttp(description, routineEndpoint.Method, routineEndpoint.Path);
                            }
                            urlDescription = string.Concat(routineEndpoint.Method.ToString(), " ", routineEndpoint.Path);
                            description = string.Concat(routineDescription, " mapped to ", urlDescription);
                        }
                    }
                }

                // request_param_type  [ [ query_string | query ] | [ body_json |  body ] ]
                // param_type  [ [ query_string | query ] | [ body_json | body ] ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ParamTypeKey))
                {
                    if (StrEqualsToArray(wordsLower[1], QueryKey))
                    {
                        routineEndpoint.RequestParamType = RequestParamType.QueryString;
                    }
                    else if (StrEqualsToArray(wordsLower[1], JsonKey))
                    {
                        routineEndpoint.RequestParamType = RequestParamType.BodyJson;
                    }
                    else
                    {
                        Logger?.InvalidParameterTypeComment(wordsLower[1], description, routineEndpoint.RequestParamType);
                    }

                    if (originalParamType != routineEndpoint.RequestParamType)
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetParameterType(description, routineEndpoint.RequestParamType);
                        }
                    }
                }

                // authorize
                // requires_authorization
                // authorize [ role1, role2, role3 [, ...] ]
                // requires_authorization [ role1, role2, role3 [, ...] ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], AuthorizeKey))
                {
                    routineEndpoint.RequiresAuthorization = true;
                    if (wordsLower.Length > 1)
                    {
                        routineEndpoint.AuthorizeRoles = [.. wordsLower[1..]];
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetAuthRoles(description, routineEndpoint.AuthorizeRoles);
                        }
                    }
                    else
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetAuth(description);
                        }
                    }
                }

                // allow_anonymous
                // anonymous
                // allow_anon
                // anon
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], AllowAnonymousKey))
                {
                    routineEndpoint.RequiresAuthorization = false;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentSetAnon(description);
                    }
                }

                // command_timeout interval
                // timeout interval
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], TimeoutKey))
                {
                    var parsedInterval = Parser.ParsePostgresInterval(wordsLower[1]);
                    if (parsedInterval is null)
                    {
                        Logger?.InvalidTimeoutComment(wordsLower[1], description, routineEndpoint.CommandTimeout);
                    }
                    else if (routineEndpoint.CommandTimeout != parsedInterval)
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetTimeout(description, parsedInterval);
                        }
                    }
                    routineEndpoint.CommandTimeout = parsedInterval;
                }

                // request_headers_mode [ ignore | context | parameter ]
                // request_headers [ ignore | context | parameter ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RequestHeadersModeKey))
                {
                    if (StrEquals(wordsLower[1], RequestHeaderModeIgnoreKey))
                    {
                        routineEndpoint.RequestHeadersMode = RequestHeadersMode.Ignore;
                    }
                    else if (StrEquals(wordsLower[1], RequestHeaderModeContextKey))
                    {
                        routineEndpoint.RequestHeadersMode = RequestHeadersMode.Context;
                    }
                    else if (StrEquals(wordsLower[1], RequestHeaderModeParameterKey))
                    {
                        routineEndpoint.RequestHeadersMode = RequestHeadersMode.Parameter;
                    }
                    else
                    {
                        Logger?.InvalidRequestHeadersModeComment(wordsLower[1], description, routineEndpoint.RequestHeadersMode);
                    }
                    if (routineEndpoint.RequestHeadersMode != Options.RequestHeadersMode)
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetRequestHeadersMode(description, wordsLower[1]);
                        }
                    }
                }

                // request_headers_parameter_name name
                // request_headers_param_name name
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RequestHeadersParameterNameKey))
                {
                    if (len == 2)
                    {
                        if (!string.Equals(routineEndpoint.RequestHeadersParameterName, wordsLower[1]))
                        {
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentSetRequestHeadersParamName(description, wordsLower[1]);
                            }
                        }
                        routineEndpoint.RequestHeadersParameterName = wordsLower[1];
                    }
                }

                // body_parameter_name name
                // body_param_name name
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], BodyParameterNameKey))
                {
                    if (len == 2)
                    {
                        if (!string.Equals(routineEndpoint.BodyParameterName, wordsLower[1]))
                        {
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentSetBodyParamName(description, wordsLower[1]);
                            }
                        }
                        routineEndpoint.BodyParameterName = wordsLower[1];
                    }
                }

                // response_null_handling [ empty_string | empty | null_literal | null | no_content | 204 | 204_no_content ]
                // response_null [ empty_string | empty | null_literal | null |  no_content | 204 | 204_no_content ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], TextResponseNullHandlingKey))
                {
                    if (StrEqualsToArray(wordsLower[1], EmptyStringKey))
                    {
                        routineEndpoint.TextResponseNullHandling = TextResponseNullHandling.EmptyString;
                    }
                    else if (StrEqualsToArray(wordsLower[1], NullLiteral))
                    {
                        routineEndpoint.TextResponseNullHandling = TextResponseNullHandling.NullLiteral;
                    }
                    else if (StrEqualsToArray(wordsLower[1], NoContentKey))
                    {
                        routineEndpoint.TextResponseNullHandling = TextResponseNullHandling.NoContent;
                    }
                    else
                    {
                        Logger?.InvalidResponseNullHandlingModeComment(wordsLower[1], description, routineEndpoint.TextResponseNullHandling);
                    }
                    if (routineEndpoint.TextResponseNullHandling != Options.TextResponseNullHandling)
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetTextResponseNullHandling(description, wordsLower[1]);
                        }
                    }
                }

                // query_string_null_handling [ empty_string | empty | null_literal | null |  ignore ]
                // query_null_handling [ empty_string | empty |null_literal | null |  ignore ]
                // query_string_null [ empty_string | empty |null_literal | null |  ignore ]
                // query_null [ empty_string | empty | null_literal | null |  ignore ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], QueryStringNullHandlingKey))
                {
                    if (StrEqualsToArray(wordsLower[1], EmptyStringKey))
                    {
                        routineEndpoint.QueryStringNullHandling = QueryStringNullHandling.EmptyString;
                    }
                    else if (StrEqualsToArray(wordsLower[1], NullLiteral))
                    {
                        routineEndpoint.QueryStringNullHandling = QueryStringNullHandling.NullLiteral;
                    }
                    else if (StrEquals(wordsLower[1], RequestHeaderModeIgnoreKey))
                    {
                        routineEndpoint.QueryStringNullHandling = QueryStringNullHandling.Ignore;
                    }
                    else
                    {
                        Logger?.InvalidQueryStringNullHandlingComment(wordsLower[1], description, routineEndpoint.QueryStringNullHandling);
                    }
                    if (routineEndpoint.TextResponseNullHandling != Options.TextResponseNullHandling)
                    {
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentSetQueryStringNullHandling(description, routineEndpoint.QueryStringNullHandling);
                        }
                    }
                }

                // login
                // signin
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], LoginKey))
                {
                    routineEndpoint.Login = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentSetLogin(description);
                    }
                }

                // logout
                // signout
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], LogoutKey))
                {
                    routineEndpoint.Logout = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentSetLogout(description);
                    }
                }

                // buffer_rows number
                // buffer number
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], BufferRowsKey))
                {
                    if (ulong.TryParse(wordsLower[1], out var parsedBuffer))
                    {
                        if (routineEndpoint.BufferRows != parsedBuffer)
                        {
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentBufferRows(description, wordsLower[1]);
                            }
                        }
                        routineEndpoint.BufferRows = parsedBuffer;
                    }
                    else
                    {
                        Logger?.InvalidBufferRows(wordsLower[1], description, Options.BufferRows);
                    }
                }

                // raw
                // raw_mode
                // raw_results
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RawKey))
                {
                    Logger?.CommentSetRawMode(description);
                    routineEndpoint.Raw = true;
                }

                // separator [ value ]
                // raw_separator [ value ]
                else if (haveTag is true && line.StartsWith(string.Concat(SeparatorKey[0], " ")))
                //else if (haveTag is true && len >= 2 && StrEqualsToArray(words[0], separatorKey))
                {
                    var sep = line[(wordsLower[0].Length + 1)..];
                    Logger?.CommentSetRawValueSeparator(description, sep);
                    routineEndpoint.RawValueSeparator = Regex.Unescape(sep);
                }

                // new_line [ value ]
                // raw_new_line [ value ]
                else if (haveTag is true && len >= 2 && line.StartsWith(string.Concat(NewLineKey[0], " ")))
                //else if (haveTag is true && len >= 2 && StrEqualsToArray(words[0], newLineKey))
                {
                    var nl = line[(wordsLower[0].Length + 1)..];
                    Logger?.CommentSetRawNewLineSeparator(description, nl);
                    routineEndpoint.RawNewLineSeparator = Regex.Unescape(nl);
                }

                // columns
                // names
                // column_names
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ColumnNamesKey))
                {
                    routineEndpoint.RawColumnNames = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentRawSetColumnNames(description);
                    }
                }

                // sensitive
                // security_sensitive
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SecuritySensitiveKey))
                {
                    routineEndpoint.SecuritySensitive = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentSecuritySensitive(description);
                    }
                }

                // user_context
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], UserContextKey))
                {
                    routineEndpoint.UserContext = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentUserContext(description);
                    }
                }

                // user_parameters
                // user_params
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], UserParemetersKey))
                {
                    routineEndpoint.UseUserParameters = true;
                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentUserParameters(description);
                    }
                }

                // cached
                // cached [ param1, param2, param3 [, ...] ]
                else if (haveTag is true && StrEquals(wordsLower[0], CacheKey))
                {
                    if (!(routine.ReturnsSet == false && routine.ColumnCount == 1 && routine.ReturnsRecordType is false))
                    {
                        Logger?.CommentInvalidCache(description);
                    }
                    routineEndpoint.Cached = true;
                    if (len > 1)
                    {
                        var names = words[1..];
                        HashSet<string> result = new(names.Length);
                        for (int j = 0; j < names.Length; j++)
                        {
                            var name = names[j];
                            if (!routine.OriginalParamsHash.Contains(name))
                            {
                                Logger?.CommentInvalidCacheParam(description, name);
                            } 
                            else
                            {
                                result.Add(name);
                            }
                        }
                        routineEndpoint.CachedParams = result;
                    }

                    if (Options.LogAnnotationSetInfo)
                    {
                        Logger?.CommentCached(description, routineEndpoint.CachedParams ?? []);
                    }
                }

                // cache_expires [ value ]
                // cache_expires_in [ value ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], CacheExpiresInKey))
                {
                    var value = Parser.ParsePostgresInterval(string.Join(Consts.Space, wordsLower[1..]));
                    if (value is not null)
                    {
                        routineEndpoint.CacheExpiresIn = value.Value;
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentCacheExpiresIn(description, value.Value);
                        }
                    }
                    else
                    {
                        Logger?.InvalidCacheExpiresIn(description, string.Join(Consts.Space, wordsLower[1..]));
                    }
                }

                // connection
                // connection_name
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ConnectionNameKey))
                {
                    var name = string.Join(Consts.Space, wordsLower[1..]);
                    if (string.IsNullOrEmpty(name) is false)
                    {
                        if (Options.ConnectionStrings is null || Options.ConnectionStrings.ContainsKey(name) is false)
                        {
                            Logger?.CommentInvalidConnectionName(description, name);
                        }
                        routineEndpoint.ConnectionName = name;
                        if (Options.LogAnnotationSetInfo)
                        {
                            Logger?.CommentConnectionName(description, name);
                        }
                    }
                    else
                    {
                        Logger?.CommentEmptyConnectionName(description);
                    }
                }

                // upload
                // upload for handler_name1, handler_name2 [, ...]
                // upload param_name as metadata
                else if (haveTag is true && StrEquals(wordsLower[0], UploadKey))
                {
                    if (Options.UploadOptions.UploadHandlers is null || Options.UploadOptions.UploadHandlers.Count == 0)
                    {
                        Logger?.CommentUploadNoHandlers(description);
                    }
                    else
                    {
                        if (routineEndpoint.Upload is false)
                        {
                            routineEndpoint.Upload = true;
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentUpload(description);
                            }
                        }
                        if (routineEndpoint.RequestParamType != RequestParamType.QueryString)
                        {
                            routineEndpoint.RequestParamType = RequestParamType.QueryString;
                        }
                        if (routineEndpoint.Method != Method.POST)
                        {
                            routineEndpoint.Method = Method.POST;
                        }
                        if (len >= 3 && StrEquals(wordsLower[1], "for"))
                        {
                            HashSet<string> existingHandlers = Options.UploadOptions.UploadHandlers?.Keys.ToHashSet() ?? [];
                            var handlers = wordsLower[2..]
                                .Select(w =>
                                {
                                    var handler = w.TrimEnd(',');
                                    bool exists = true;
                                    if (existingHandlers.Contains(handler) is false)
                                    {
                                        Logger?.CommentUploadHandlerNotExists(description, handler, existingHandlers);
                                        exists = false;
                                    }
                                    return new { exists, handler };
                                })
                                .Where(x => x.exists is true)
                                .Select(x => x.handler)
                                .ToArray();

                            routineEndpoint.UploadHandlers = handlers;
                            if (Options.LogAnnotationSetInfo)
                            {
                                if (handlers.Length == 0)
                                {
                                    var first = Options.UploadOptions.UploadHandlers?.Keys.FirstOrDefault();
                                    Logger?.CommentUploadFirstAvaialbleHandler(description, first);
                                }
                                if (handlers.Length == 1)
                                {
                                    Logger?.CommentUploadSingleHandler(description, handlers[0]);
                                }
                                else
                                {
                                    Logger?.CommentUploadHandlers(description, handlers);
                                }
                            }
                        }

                        else if (len >= 4 && StrEquals(wordsLower[2], "as") && StrEquals(wordsLower[3], "metadata"))
                        {
                            var paramName = wordsLower[1];
                            NpgsqlRestParameter? param = routine.Parameters.FirstOrDefault(x =>
                                    string.Equals(x.ActualName, paramName, StringComparison.Ordinal) ||
                                    string.Equals(x.ConvertedName, paramName, StringComparison.Ordinal));
                            if (param is null)
                            {
                                Logger?.CommentUploadWrongMetadataParam(description, paramName);
                            }
                            else
                            {
                                param.IsUploadMetadata = true;
                                if (Options.LogAnnotationSetInfo)
                                {
                                    Logger?.CommentUploadMetadataParam(description, paramName);
                                }
                            }
                        }
                    }
                }

                // param param_name1 is hash of param_name2
                // param param_name is upload metadata
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ParameterKey))
                {
                    // param param_name1 is hash of param_name2
                    if (len >= 6 && StrEquals(wordsLower[2], "is") && StrEquals(wordsLower[3], "hash") && StrEquals(wordsLower[4], "of"))
                    {
                        var paramName1 = wordsLower[1];
                        var paramName2 = wordsLower[5];

                        var found = true;
                        NpgsqlRestParameter? param = null;

                        if (routine.OriginalParamsHash.Contains(paramName1) is false &&
                            routine.ParamsHash.Contains(paramName1) is false)
                        {
                            Logger?.CommentParamNotExistsCantHash(description, paramName1);
                            found = false;
                        }

                        if (found is true &&
                            routine.OriginalParamsHash.Contains(paramName2) is false &&
                            routine.ParamsHash.Contains(paramName2) is false)
                        {
                            Logger?.CommentParamNotExistsCantHash(description, paramName2);
                            found = false;
                        }

                        if (found is true)
                        {
                            param = routine.Parameters.FirstOrDefault(x =>
                                string.Equals(x.ActualName, paramName1, StringComparison.Ordinal) ||
                                string.Equals(x.ConvertedName, paramName1, StringComparison.Ordinal));
                            if (param is not null)
                            {
                                param.HashOf = routine.Parameters.FirstOrDefault(x =>
                                    string.Equals(x.ActualName, paramName2, StringComparison.Ordinal) ||
                                    string.Equals(x.ConvertedName, paramName2, StringComparison.Ordinal));
                                if (param.HashOf is null)
                                {
                                    Logger?.CommentParamNotExistsCantHash(description, paramName2);
                                }
                                else
                                {
                                    if (Options.LogAnnotationSetInfo)
                                    {
                                        Logger?.CommentParamIsHashOf(description, paramName1, paramName2);
                                    }
                                }
                            }
                            else
                            {
                                Logger?.CommentParamNotExistsCantHash(description, paramName1);
                            }
                        }
                    }

                    // param param_name1 is upload metadata
                    if (len >= 5 && (
                        StrEquals(wordsLower[2], "is") && StrEquals(wordsLower[3], "upload") && StrEquals(wordsLower[4], "metadata")
                        ))
                    {
                        if (routineEndpoint.Upload is false)
                        {
                            routineEndpoint.Upload = true;
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentUpload(description);
                            }
                        }
                        if (routineEndpoint.RequestParamType != RequestParamType.QueryString)
                        {
                            routineEndpoint.RequestParamType = RequestParamType.QueryString;
                        }
                        if (routineEndpoint.Method != Method.POST)
                        {
                            routineEndpoint.Method = Method.POST;
                        }

                        var paramName = wordsLower[1];
                        NpgsqlRestParameter? param = routine.Parameters.FirstOrDefault(x =>
                                string.Equals(x.ActualName, paramName, StringComparison.Ordinal) ||
                                string.Equals(x.ConvertedName, paramName, StringComparison.Ordinal));
                        if (param is null)
                        {
                            Logger?.CommentUploadWrongMetadataParam(description, paramName);
                        }
                        else
                        {
                            param.IsUploadMetadata = true;
                            if (Options.LogAnnotationSetInfo)
                            {
                                Logger?.CommentUploadMetadataParam(description, paramName);
                            }
                        }
                    }
                }

                // info_path [ true | false | path ]
                // info_events_path [ true | false | path ]
                // info_streaming_path [ true |false | path ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], InfoEventsStreamingPathKey))
                {
                    if (bool.TryParse(wordsLower[1], out var parseredStreamingPath))
                    {
                        if (parseredStreamingPath is true)
                        {
                            routineEndpoint.InfoEventsStreamingPath = Consts.DefaultInfoPath;
                        }
                    }
                    else
                    {
                        routineEndpoint.InfoEventsStreamingPath = wordsLower[1];
                    }
                    Logger?.CommentInfoStreamingPath(description, routineEndpoint.InfoEventsStreamingPath);
                }

                // info_scope [ [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ] ] 
                // info_events_scope [ [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ] ] 
                // info_streaming_scope [ [ self | matching | authorize | all ] | [ authorize [ role1, role2, role3 [, ...] ] ] ] 
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], InfoEventsStreamingScopeKey))
                {
                    if (wordsLower.Length > 1 && Enum.TryParse<InfoEventsScope>(wordsLower[1], true, out var parsedScope))
                    {
                        routineEndpoint.InfoEventsScope = parsedScope;
                        if (parsedScope == InfoEventsScope.Authorize && wordsLower.Length > 2)
                        {
                            routineEndpoint.InfoEventsRoles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var word in wordsLower[2..])
                            {
                                if (string.IsNullOrWhiteSpace(word) is false)
                                {
                                    routineEndpoint.InfoEventsRoles.Add(word);
                                }
                            }
                            Logger?.CommentInfoStreamingScopeRoles(description, routineEndpoint.InfoEventsRoles);
                        }
                        else
                        {
                            Logger?.CommentInfoStreamingScope(description, routineEndpoint.InfoEventsScope);
                        }
                    }
                    else
                    {
                        Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.",
                            wordsLower[0], string.Join(", ", Enum.GetNames<InfoEventsScope>()), line);
                    }
                }

                // basic_authentication [ username ] [ password ]
                // basic_auth [ username ] [ password ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], BasicAuthKey))
                {
                    if (routineEndpoint.BasicAuth is null)
                    {
                        routineEndpoint.BasicAuth = new() { Enabled = true };
                        Logger?.BasicAuthEnabled(description);
                    }

                    if (len >= 3)
                    {
                        var username = words[1];
                        var password = words[2];
                        if (string.IsNullOrEmpty(username) is false && string.IsNullOrEmpty(password) is false)
                        {
                            routineEndpoint.BasicAuth.Users[username] = password;
                            Logger?.BasicAuthUserAdded(description, username);
                        }
                    }
                    else
                    {
                        Logger?.BasicAuthUserFailed(description);
                    }
                }
                
                // basic_authentication_realm [ realm ]
                // basic_auth_realm [ realm ]
                // realm [ realm ]
                else if (haveTag is true && len > 1 && StrEqualsToArray(wordsLower[0], BasicAuthRealmKey))
                {
                    if (routineEndpoint.BasicAuth is null)
                    {
                        routineEndpoint.BasicAuth = new() { Enabled = true };
                        Logger?.BasicAuthEnabled(description);
                    }
                    routineEndpoint.BasicAuth.Realm = words[1];
                    Logger?.BasicAuthRealmSet(description, routineEndpoint.BasicAuth.Realm);
                }

                // basic_authentication_command [ command ]
                // basic_auth_command [ command ]
                // challenge_command [ command ]
                else if (haveTag is true && len > 1 && StrEqualsToArray(words[0], BasicAuthCommandKey))
                {
                    if (routineEndpoint.BasicAuth is null)
                    {
                        routineEndpoint.BasicAuth = new() { Enabled = true };
                        Logger?.BasicAuthEnabled(description);
                    }
                    routineEndpoint.BasicAuth.ChallengeCommand = line[(words[0].Length + 1)..];
                    Logger?.BasicAuthChallengeCommandSet(description, routineEndpoint.BasicAuth.ChallengeCommand);
                }

                // retry_strategy_name [ name ]
                // retry_strategy [ name ]
                // retry [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], RetryStrategyKey))
                {
                    var name = string.Join(Consts.Space, words[1..]);
                    if (Options.CommandRetryOptions.Strategies.TryGetValue(name, out var strategy))
                    {
                        routineEndpoint.RetryStrategy = strategy;
                        Logger?.RetryStrategySet(description, name);
                    }
                    else
                    {
                        Logger?.RetryStrategyNotFound(description, name);
                    }
                }
                
                // rate_limiter_policy_name [ name ]
                // rate_limiter_policy [ name ]
                // rate_limiter [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], RateLimiterPolicyKey))
                {
                    routineEndpoint.RateLimiterPolicy = string.Join(Consts.Space, words[1..]);
                    Logger?.RateLimiterPolicySet(description, routineEndpoint.RateLimiterPolicy);
                }
                
                // error_code_policy_name [ name ]
                // error_code_policy [ name ]
                // error_code [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ErrorCodePolicyKey))
                {
                    routineEndpoint.ErrorCodePolicy = string.Join(Consts.Space, words[1..]);
                    Logger?.ErrorCodePolicySet(description, routineEndpoint.ErrorCodePolicy);
                }
            }
            if (disabled)
            {
                if (Options.LogAnnotationSetInfo)
                {
                    Logger?.CommentDisabled(description);
                }
                return null;
            }
            if (Options.CommentsMode == CommentsMode.OnlyWithHttpTag && !hasHttpTag)
            {
                return null;
            }
        }

        return routineEndpoint;
    }

    public static void SetCustomParameter(RoutineEndpoint endpoint, string name, string value)
    {
        value = Regex.Unescape(value);

        if (StrEqualsToArray(name, BufferRowsKey))
        {
            if (ulong.TryParse(value, out var parsedBuffer))
            {
                endpoint.BufferRows = parsedBuffer;
            }
        }
        else if (StrEqualsToArray(name, RawKey))
        {
            if (bool.TryParse(value, out var parsedRaw))
            {
                endpoint.Raw = parsedRaw;
            }
        }
        else if (StrEqualsToArray(name, SeparatorKey))
        {
            endpoint.RawValueSeparator = value;
        }
        else if (StrEqualsToArray(name, NewLineKey))
        {
            endpoint.RawNewLineSeparator = value;
        }
        else if (StrEqualsToArray(name, ColumnNamesKey))
        {
            if (bool.TryParse(value, out var parsedRawColumnNames))
            {
                endpoint.RawColumnNames = parsedRawColumnNames;
            }
        }
        else if (StrEqualsToArray(name, ConnectionNameKey))
        {
            //if (Options.ConnectionStrings is not null && options.ConnectionStrings.ContainsKey(value) is true)
            //{
            //    endpoint.ConnectionName = value;
            //}
            endpoint.ConnectionName = value;
        }
        else if (StrEqualsToArray(name, UserContextKey))
        {
            if (bool.TryParse(value, out var parserUserContext))
            {
                endpoint.UserContext = parserUserContext;
            }
        }
        else if (StrEqualsToArray(name, UserParemetersKey))
        {
            if (bool.TryParse(value, out var parserUserParameters))
            {
                endpoint.UseUserParameters = parserUserParameters;
            }
        }

        else if (StrEqualsToArray(name, InfoEventsStreamingPathKey))
        {
            if (bool.TryParse(value, out var parseredStreamingPath))
            {
                if (parseredStreamingPath is true)
                {
                    endpoint.InfoEventsStreamingPath = Consts.DefaultInfoPath;
                }
            }
            else
            {
                endpoint.InfoEventsStreamingPath = value;
            }
        }

        else if (StrEqualsToArray(name, InfoEventsStreamingScopeKey))
        {
            var words = value.SplitWords();
            if (words.Length > 0 && Enum.TryParse<InfoEventsScope>(words[0], true, out var parsedScope))
            {
                endpoint.InfoEventsScope = parsedScope;
                if (parsedScope == InfoEventsScope.Authorize && words.Length > 1)
                {
                    endpoint.InfoEventsRoles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var word in words[1..])
                    {
                        if (string.IsNullOrWhiteSpace(word) is false)
                        {
                            endpoint.InfoEventsRoles.Add(word);
                        }
                    }
                }
            }

            else
            {
                Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.", 
                    name, string.Join(", ", Enum.GetNames<InfoEventsScope>()), value);
            }
        }
        
        else if (StrEqualsToArray(name, BasicAuthKey))
        {
            if (endpoint.BasicAuth is null)
            {
                endpoint.BasicAuth = new() { Enabled = true };
            }
            var words = value.SplitWords();
            if (words.Length >= 3)
            {
                var username = words[1];
                var password = words[2];
                if (string.IsNullOrEmpty(username) is false && string.IsNullOrEmpty(password) is false)
                {
                    endpoint.BasicAuth.Users[username] = password;
                }
            }
        }

        else if (StrEqualsToArray(name, BasicAuthRealmKey))
        {
            if (endpoint.BasicAuth is null)
            {
                endpoint.BasicAuth = new() { Enabled = true };
            }
            endpoint.BasicAuth.Realm = value;
        }
        
        else if (StrEqualsToArray(name, BasicAuthCommandKey))
        {
            if (endpoint.BasicAuth is null)
            {
                endpoint.BasicAuth = new() { Enabled = true };
            }
            endpoint.BasicAuth.ChallengeCommand = value;
        }
        
        else if (StrEqualsToArray(name, RetryStrategyKey))
        {
            if (Options.CommandRetryOptions.Strategies.TryGetValue(value, out var strategy))
            {
                endpoint.RetryStrategy = strategy;
            }
        }
        
        else if (StrEqualsToArray(name, RateLimiterPolicyKey))
        {
            endpoint.RateLimiterPolicy = value;
        }
        
        else if (StrEqualsToArray(name, ErrorCodePolicyKey))
        {
            endpoint.ErrorCodePolicy = value;
        }
        
        else if (StrEqualsToArray(name, TimeoutKey))
        {
            var parsedInterval = Parser.ParsePostgresInterval(value);
            if (parsedInterval is not null)
            {
                endpoint.CommandTimeout = parsedInterval;
            }
        }
        
        else
        {
            if (endpoint.CustomParameters is null)
            {
                endpoint.CustomParameters = new()
                {
                    [name] = value
                };
            }
            else
            {
                endpoint.CustomParameters[name] = value;
            }
        }
    }

    public static bool StrEquals(string str1, string str2) => str1.Equals(str2, StringComparison.OrdinalIgnoreCase);

    public static bool StrEqualsToArray(string str, params string[] arr)
    {
        for (var i = 0; i < arr.Length; i++)
        {
            if (str.Equals(arr[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static string[] SplitWordsLower(this string str)
    {
        if (str is null)
        {
            return [];
        }
        return [.. str
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
        ];
    }
    
    public static string[] SplitWords(this string str)
    {
        if (str is null)
        {
            return [];
        }
        return [.. str
            .Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
        ];
    }

    public static bool SplitBySeparatorChar(string str, char sep, out string part1, out string part2)
    {
        part1 = null!;
        part2 = null!;
        if (str.Contains(sep) is false)
        {
            return false;
        }

        var parts = str.Split(sep, 2);
        if (parts.Length == 2)
        {
            part1 = parts[0].Trim();
            part2 = parts[1].Trim();
            if (ContainsValidNameCharacter(part1))
            {
                return false;
            }
            return true;
        }
        return false;
    }

    public static bool ContainsValidNameCharacter(string input)
    {
        foreach (char c in input)
        {
            if (char.IsLetterOrDigit(c) is false && c != '-' && c != '_')
            {
                return true;
            }
        }
        return false;
    }
}