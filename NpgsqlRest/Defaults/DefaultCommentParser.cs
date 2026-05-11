using System.Text.RegularExpressions;

namespace NpgsqlRest.Defaults;

internal static partial class DefaultCommentParser
{
    private static readonly char[] NewlineSeparator = ['\r', '\n'];
    private static readonly char[] WordSeparators = [Consts.Space, Consts.Comma];

    // All annotation keys moved to their respective handler files in CommentParsers directory

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
            var routineDescription = routine.Type == RoutineType.SqlFile
                ? routine.SimpleDefinition
                : string.Concat(routine.Type, " ", routine.Schema, ".", routine.Name);
            var urlDescription = string.Concat(routineEndpoint.Method.ToString(), " ", routineEndpoint.Path);
            var description = string.Concat(routineDescription, " mapped to ", urlDescription);

            _annotationLabels = new List<string>();

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
                    HandleTags(routine, wordsLower, ref haveTag);
                }

                // key = value
                // If key matches a real parameter name → resolved parameter (SQL expression)
                // Otherwise → custom parameter
                else if (haveTag is true && SplitBySeparatorChar(line, Consts.Equal, out var customParamName, out var customParamValue))
                {
                    if (routine.OriginalParamsHash.Contains(customParamName))
                    {
                        HandleResolvedParameter(routineEndpoint, customParamName, customParamValue, description);
                    }
                    else
                    {
                        HandleCustomParameter(routineEndpoint, customParamName, customParamValue, description);
                    }
                    TrackAnnotation(line);
                }

                // key: value
                // Content-Type: application/json
                else if (haveTag is true && SplitBySeparatorChar(line, Consts.Colon, out var headerName, out var headerValue))
                {
                    HandleHeader(routineEndpoint, headerName, headerValue, description);
                    TrackAnnotation(line);
                }

                // disabled
                // disabled tag1, tag2, tag3 [, ...]
                else if (haveTag is true && StrEquals(wordsLower[0], DisabledKey))
                {
                    HandleDisabled(routine, wordsLower, len, ref disabled);
                    TrackAnnotation(line);
                }

                // enabled
                // enabled [ tag1, tag2, tag3 [, ...] ]
                else if (haveTag is true && StrEquals(wordsLower[0], EnabledKey))
                {
                    HandleEnabled(routine, wordsLower, len, ref disabled);
                    TrackAnnotation(line);
                }

                // internal
                // internal_only
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], InternalKey))
                {
                    routineEndpoint.InternalOnly = true;
                    CommentLogger?.LogTrace("Endpoint {Description} marked as internal-only", description);
                    TrackAnnotation(line);
                }

                // openapi
                // openapi hide | hidden | ignore
                // openapi tag tag1, tag2, ...
                // openapi tags tag1, tag2, ...
                else if (haveTag is true && StrEquals(wordsLower[0], OpenApiKey))
                {
                    HandleOpenApi(routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // HTTP
                // HTTP [ GET | POST | PUT | DELETE ]
                // HTTP [ GET | POST | PUT | DELETE ] path
                // HTTP path
                else if (haveTag is true && StrEquals(wordsLower[0], HttpKey))
                {
                    HandleHttp(routineEndpoint, wordsLower, len, description, ref urlDescription, ref description, routineDescription, ref hasHttpTag);
                    TrackAnnotation(line);
                }

                // PATH path
                else if (haveTag is true && StrEquals(wordsLower[0], PathKey))
                {
                    HandlePath(routineEndpoint, wordsLower, description, ref urlDescription, ref description, routineDescription);
                    TrackAnnotation(line);
                }

                // request_param_type  [ [ query_string | query ] | [ body_json |  body ] ]
                // param_type  [ [ query_string | query ] | [ body_json | body ] ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ParamTypeKey))
                {
                    HandleParamType(routineEndpoint, wordsLower, description, originalParamType);
                    TrackAnnotation(line);
                }

                // authorize
                // requires_authorization
                // authorize [ role1, role2, role3 [, ...] ]
                // requires_authorization [ role1, role2, role3 [, ...] ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], AuthorizeKey))
                {
                    HandleAuthorize(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // allow_anonymous
                // anonymous
                // allow_anon
                // anon
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], AllowAnonymousKey))
                {
                    HandleAllowAnonymous(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // command_timeout interval
                // timeout interval
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], TimeoutKey))
                {
                    HandleTimeout(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // request_headers_mode [ ignore | context | parameter ]
                // request_headers [ ignore | context | parameter ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RequestHeadersModeKey))
                {
                    HandleRequestHeadersMode(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // request_headers_parameter_name name
                // request_headers_param_name name
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RequestHeadersParameterNameKey))
                {
                    HandleRequestHeadersParameterName(routineEndpoint, wordsLower, len, description);
                    TrackAnnotation(line);
                }

                // body_parameter_name name
                // body_param_name name
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], BodyParameterNameKey))
                {
                    HandleBodyParameterName(routineEndpoint, wordsLower, len, description);
                    TrackAnnotation(line);
                }

                // response_null_handling [ empty_string | empty | null_literal | null | no_content | 204 | 204_no_content ]
                // response_null [ empty_string | empty | null_literal | null |  no_content | 204 | 204_no_content ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], TextResponseNullHandlingKey))
                {
                    HandleResponseNullHandling(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // query_string_null_handling [ empty_string | empty | null_literal | null |  ignore ]
                // query_null_handling [ empty_string | empty |null_literal | null |  ignore ]
                // query_string_null [ empty_string | empty |null_literal | null |  ignore ]
                // query_null [ empty_string | empty | null_literal | null |  ignore ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], QueryStringNullHandlingKey))
                {
                    HandleQueryStringNullHandling(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // login
                // signin
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], LoginKey))
                {
                    HandleLogin(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // logout
                // signout
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], LogoutKey))
                {
                    HandleLogout(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // buffer_rows number
                // buffer number
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], BufferRowsKey))
                {
                    HandleBufferRows(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // raw
                // raw_mode
                // raw_results
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], RawKey))
                {
                    HandleRaw(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // single
                // single_record
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SingleKey))
                {
                    HandleSingle(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // void
                // void_result
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], VoidKey))
                {
                    HandleVoid(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // separator [ value ]
                // raw_separator [ value ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SeparatorKey))
                {
                    HandleSeparator(routineEndpoint, line, wordsLower, description);
                    TrackAnnotation(line);
                }

                // new_line [ value ]
                // raw_new_line [ value ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], NewLineKey))
                {
                    HandleNewLine(routineEndpoint, line, wordsLower, description);
                    TrackAnnotation(line);
                }

                // columns
                // names
                // column_names
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ColumnNamesKey))
                {
                    HandleColumnNames(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // sensitive
                // security_sensitive
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SecuritySensitiveKey))
                {
                    HandleSecuritySensitive(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // user_context
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], UserContextKey))
                {
                    HandleUserContext(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // user_parameters
                // user_params
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], UserParemetersKey))
                {
                    HandleUserParameters(routineEndpoint, description);
                    TrackAnnotation(line);
                }

                // cached
                // cached [ param1, param2, param3 [, ...] ]
                else if (haveTag is true && StrEquals(wordsLower[0], CacheKey))
                {
                    HandleCached(routine, routineEndpoint, words, len, description);
                    TrackAnnotation(line);
                }

                // cache_expires [ value ]
                // cache_expires_in [ value ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], CacheExpiresInKey))
                {
                    HandleCacheExpiresIn(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // cache_profile <name>
                else if (haveTag is true && StrEquals(wordsLower[0], CacheProfileKey))
                {
                    HandleCacheProfile(routineEndpoint, words, len, description);
                    TrackAnnotation(line);
                }

                // connection
                // connection_name
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ConnectionNameKey))
                {
                    HandleConnectionName(routineEndpoint, wordsLower, description);
                    TrackAnnotation(line);
                }

                // upload
                // upload for handler_name1, handler_name2 [, ...]
                // upload param_name as metadata
                else if (haveTag is true && StrEquals(wordsLower[0], UploadKey))
                {
                    HandleUpload(routine, routineEndpoint, wordsLower, len, description);
                    TrackAnnotation(line);
                }

                // param param_name1 is hash of param_name2
                // param param_name is upload metadata
                // param [original] [new_name] [type]
                // param [original] is [new_name] [type is [type]]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ParameterKey))
                {
                    HandleParameter(routine, routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // sse path [ path ] [ on info | notice | warning ]
                // sse_path [ path ] [ on info | notice | warning ]
                // sse_events_path [ path ] [ on info | notice | warning ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SseEventsStreamingPathKey))
                {
                    HandleSseEventsPath(routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // sse_publish [ on info | notice | warning ]
                // sse_events_publish [ on info | notice | warning ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SseEventsPublishKey))
                {
                    HandleSseEventsPublish(routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // sse_subscribe [ path ] [ on info | notice | warning ]
                // sse_events_subscribe [ path ] [ on info | notice | warning ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], SseEventsSubscribeKey))
                {
                    HandleSseEventsSubscribe(routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // sse_level [ info | notice | warning ]
                // sse_events_level [ info | notice | warning ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], SseEventsLevelKey))
                {
                    HandleSseEventsLevel(routineEndpoint, wordsLower, words, line, description);
                    TrackAnnotation(line);
                }

                // sse_scope [ [ matching | authorize | all ] | [ authorize [ role_or_user1, role_or_user1, role_or_user1 [, ...] ] ] ]
                // sse_events_scope [ [ matching | authorize | all ] | [ authorize [ role_or_user1, role_or_user1, role_or_user1 [, ...] ] ] ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], SseEventsStreamingScopeKey))
                {
                    HandleSseEventsScope(routineEndpoint, wordsLower, line, description);
                    TrackAnnotation(line);
                }

                // basic_authentication [ username ] [ password ]
                // basic_auth [ username ] [ password ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], BasicAuthKey))
                {
                    HandleBasicAuth(routineEndpoint, words, len, description);
                    TrackAnnotation(line);
                }

                // basic_authentication_realm [ realm ]
                // basic_auth_realm [ realm ]
                // realm [ realm ]
                else if (haveTag is true && len > 1 && StrEqualsToArray(wordsLower[0], BasicAuthRealmKey))
                {
                    HandleBasicAuthRealm(routineEndpoint, words, description);
                    TrackAnnotation(line);
                }

                // basic_authentication_command [ command ]
                // basic_auth_command [ command ]
                // challenge_command [ command ]
                else if (haveTag is true && len > 1 && StrEqualsToArray(words[0], BasicAuthCommandKey))
                {
                    HandleBasicAuthCommand(routineEndpoint, words, line, description);
                    TrackAnnotation(line);
                }

                // retry_strategy_name [ name ]
                // retry_strategy [ name ]
                // retry [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], RetryStrategyKey))
                {
                    HandleRetryStrategy(routineEndpoint, words, description);
                    TrackAnnotation(line);
                }

                // rate_limiter_policy_name [ name ]
                // rate_limiter_policy [ name ]
                // rate_limiter [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], RateLimiterPolicyKey))
                {
                    HandleRateLimiterPolicy(routineEndpoint, words, description);
                    TrackAnnotation(line);
                }

                // error_code_policy_name [ name ]
                // error_code_policy [ name ]
                // error_code [ name ]
                else if (haveTag is true && len >= 2 && StrEqualsToArray(wordsLower[0], ErrorCodePolicyKey))
                {
                    HandleErrorCodePolicy(routineEndpoint, words, description);
                    TrackAnnotation(line);
                }

                // validate _param_name using rule_name
                // validation _param_name using rule_name
                else if (haveTag is true && len >= 4 && StrEqualsToArray(wordsLower[0], ValidateKey))
                {
                    HandleValidate(routine, routineEndpoint, words, len, description);
                    TrackAnnotation(line);
                }

                // proxy
                // proxy [ GET | POST | PUT | DELETE | PATCH ]
                // proxy host_url
                // proxy [ GET | POST | PUT | DELETE | PATCH ] host_url
                // reverse_proxy [ ... ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ProxyKey))
                {
                    HandleProxy(routine, routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // proxy_out [ GET | POST | PUT | DELETE | PATCH ] [ host_url ] [ ?query={param} ]
                // forward_proxy [ ... ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], ProxyOutKey))
                {
                    HandleProxyOut(routine, routineEndpoint, wordsLower, words, len, description);
                    TrackAnnotation(line);
                }

                // encrypt
                // encrypt [ param1, param2, ... ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], EncryptKey))
                {
                    HandleEncrypt(routine, routineEndpoint, wordsLower, len, description);
                    TrackAnnotation(line);
                }

                // decrypt
                // decrypt [ col1, col2, ... ]
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], DecryptKey))
                {
                    HandleDecrypt(routineEndpoint, wordsLower, len, description);
                    TrackAnnotation(line);
                }

                // nested
                // nested_json
                // nested_composite
                else if (haveTag is true && StrEqualsToArray(wordsLower[0], NestedJsonKey))
                {
                    HandleNestedJson(routineEndpoint, description);
                    TrackAnnotation(line);
                }
            }
            if (disabled)
            {
                Logger?.CommentDisabled(description);
                return null;
            }
            if (Options.CommentsMode == CommentsMode.OnlyWithHttpTag && !hasHttpTag)
            {
                return null;
            }

            // Detect proxy response parameters after all annotations are processed.
            // This must run after param renames so that renamed parameters (e.g., $1 → _proxy_body)
            // are detected correctly regardless of annotation order.
            if (routineEndpoint.IsProxy)
            {
                DetectProxyResponseParameters(routine, routineEndpoint);
                Logger?.LogDebug("Proxy endpoint {Description} HasProxyResponseParameters: {HasParams}",
                    description, routineEndpoint.HasProxyResponseParameters);
            }

            // For SQL file routines: update column type descriptors when @param annotations
            // retyped parameters. Describe resolves column types before annotations run, so columns
            // from "select $1 as col" are typed as text when the parameter type was unknown.
            // After "param $1 _name integer" retypes to integer, the column descriptor should match.
            if (routine.Type == RoutineType.SqlFile)
            {
                UpdateColumnDescriptorsFromRetypedParams(routine);
            }

            if (_annotationLabels is { Count: > 0 })
            {
                Logger?.CommentAnnotationsSummary(description, string.Join(", ", _annotationLabels));
            }
            _annotationLabels = null;
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
        else if (StrEqualsToArray(name, SingleKey))
        {
            if (bool.TryParse(value, out var parsedSingle))
            {
                endpoint.ReturnSingleRecord = parsedSingle;
            }
        }
        else if (StrEqualsToArray(name, VoidKey))
        {
            if (bool.TryParse(value, out var parsedVoid))
            {
                endpoint.Void = parsedVoid;
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

        else if (StrEqualsToArray(name, SseEventsStreamingPathKey))
        {
            // Tag-value form of @sse: same shorthand semantics — publish + subscribe on the same path.
            if (bool.TryParse(value, out var parseredStreamingPath))
            {
                endpoint.SseEventsPath = parseredStreamingPath is true ?
                    (endpoint.SseEventNoticeLevel ?? Options.DefaultSseEventNoticeLevel).ToString().ToLowerInvariant() :
                    null;
                endpoint.SsePublishEnabled = parseredStreamingPath;
            }
            else
            {
                endpoint.SseEventsPath = value;
                endpoint.SsePublishEnabled = true;
            }
        }

        else if (StrEqualsToArray(name, SseEventsPublishKey))
        {
            if (bool.TryParse(value, out var parsedPublish))
            {
                endpoint.SsePublishEnabled = parsedPublish;
            }
            else
            {
                endpoint.SsePublishEnabled = true;
            }
        }

        else if (StrEqualsToArray(name, SseEventsSubscribeKey))
        {
            if (bool.TryParse(value, out var parsedSubscribe))
            {
                endpoint.SseEventsPath = parsedSubscribe is true ?
                    (endpoint.SseEventNoticeLevel ?? Options.DefaultSseEventNoticeLevel).ToString().ToLowerInvariant() :
                    null;
            }
            else
            {
                endpoint.SseEventsPath = value;
            }
        }

        else if (StrEqualsToArray(name, SseEventsLevelKey))
        {
            if (Enum.TryParse<PostgresNoticeLevels>(value, true, out var parsedLevel))
            {
                endpoint.SseEventNoticeLevel = parsedLevel;
            }
        }

        else if (StrEqualsToArray(name, SseEventsStreamingScopeKey))
        {
            var words = value.SplitWords();
            if (words.Length > 0 && Enum.TryParse<SseEventsScope>(words[0], true, out var parsedScope))
            {
                endpoint.SseEventsScope = parsedScope;
                if (parsedScope == SseEventsScope.Authorize && words.Length > 1)
                {
                    endpoint.SseEventsRoles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var word in words[1..])
                    {
                        if (string.IsNullOrWhiteSpace(word) is false)
                        {
                            endpoint.SseEventsRoles.Add(word);
                        }
                    }
                }
            }

            else
            {
                Logger?.LogError("Could not recognize valid value for parameter key {key}. Valid values are: {values}. Provided value is {provided}.", 
                    name, string.Join(", ", Enum.GetNames<SseEventsScope>()), value);
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

        else if (StrEqualsToArray(name, EncryptKey))
        {
            if (bool.TryParse(value, out var parsedEncrypt))
            {
                endpoint.EncryptAllParameters = parsedEncrypt;
            }
        }

        else if (StrEqualsToArray(name, DecryptKey))
        {
            if (bool.TryParse(value, out var parsedDecrypt))
            {
                endpoint.DecryptAllColumns = parsedDecrypt;
            }
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
            // Strip @ prefix from custom parameter names for consistency
            var paramName = name.Length > 0 && name[0] == '@' ? name[1..] : name;
            if (endpoint.CustomParameters is null)
            {
                endpoint.CustomParameters = new()
                {
                    [paramName] = value
                };
            }
            else
            {
                endpoint.CustomParameters[paramName] = value;
            }
        }
    }

    // Utility methods moved to DefaultCommentParser.Utilities.cs
}