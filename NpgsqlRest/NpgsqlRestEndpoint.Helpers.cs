using System.Data;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlRest.Proxy;
using static System.Net.Mime.MediaTypeNames;

namespace NpgsqlRest;

public partial class NpgsqlRestEndpoint
{
    /// <summary>
    /// Result of evaluating <see cref="RoutineEndpoint.CacheWhen"/> against the resolved parameter values.
    /// Three possible outcomes:
    /// - <c>Skip = true</c> → bypass cache for this request (no read, no write).
    /// - <c>Skip = false</c>, <c>TtlOverride</c> set → use this TTL when writing.
    /// - <c>Skip = false</c>, <c>TtlOverride</c> null → no rule matched; fall through to endpoint's default expiration.
    /// </summary>
    private readonly record struct CacheWhenResult(bool Skip, TimeSpan? TtlOverride);

    /// <summary>
    /// Walks <see cref="RoutineEndpoint.CacheWhen"/> in order, returning the first rule's resolution.
    /// First match wins. Lenient: rules referencing unknown routine parameter names are skipped silently
    /// (startup logs a Warning per misuse). Parameters absent from <paramref name="command"/> (request omitted them
    /// and NpgsqlRest deferred to the PG-side default) are treated as null for matching purposes.
    /// </summary>
    private static CacheWhenResult EvaluateCacheWhenRules(RoutineEndpoint endpoint, NpgsqlCommand command)
    {
        if (endpoint.CacheWhen is null || endpoint.CacheWhen.Length == 0)
        {
            return default;
        }

        foreach (var rule in endpoint.CacheWhen)
        {
            // Verify the named param exists on the routine. Skip silently if not (startup already warned).
            bool paramExistsInRoutine = false;
            for (int i = 0; i < endpoint.Routine.Parameters.Length; i++)
            {
                var rp = endpoint.Routine.Parameters[i];
                if (string.Equals(rp.ActualName, rule.Parameter, StringComparison.Ordinal) ||
                    string.Equals(rp.ConvertedName, rule.Parameter, StringComparison.Ordinal))
                {
                    paramExistsInRoutine = true;
                    break;
                }
            }
            if (!paramExistsInRoutine)
            {
                continue;
            }

            object? valueToCompare = null;
            for (int i = 0; i < command.Parameters.Count; i++)
            {
                if (command.Parameters[i] is NpgsqlRestParameter p &&
                    (string.Equals(p.ActualName, rule.Parameter, StringComparison.Ordinal) ||
                     string.Equals(p.ConvertedName, rule.Parameter, StringComparison.Ordinal)))
                {
                    valueToCompare = p.Value;
                    break;
                }
            }

            if (CacheWhenRuleMatches(valueToCompare, rule.Value))
            {
                if (rule.Skip)
                {
                    Logger?.CacheSkippedDueToWhenRule(string.Concat(endpoint.Method.ToString(), " ", endpoint.Path), rule.Parameter);
                }
                return new CacheWhenResult(rule.Skip, rule.ThenExpiration);
            }
        }
        return default;
    }

    /// <summary>
    /// Compares one parameter value against one When-rule condition. Semantics:
    /// - JSON <c>null</c> matches .NET <c>null</c> and <see cref="DBNull.Value"/> (does NOT match empty string).
    /// - JSON array: OR over entries.
    /// - Any non-null scalar (string, number, boolean): both sides are stringified and compared
    ///   case-insensitive ordinal. This matches JSON <c>true</c> against .NET <c>"True"</c> and is lenient
    ///   for case-only differences in user-typed string values.
    ///
    /// Note: when <see cref="CacheProfile.When"/> is loaded from JSON config (NpgsqlRestClient), all values
    /// arrive as strings via <c>IConfiguration</c> regardless of their original JSON type. The case-insensitive
    /// comparison preserves the intuitive matching behavior across that boundary.
    /// </summary>
    private static bool CacheWhenRuleMatches(object? value, JsonNode? condition)
    {
        if (condition is null)
        {
            return value is null || value == DBNull.Value;
        }
        if (condition is JsonArray arr)
        {
            foreach (var entry in arr)
            {
                if (CacheWhenRuleMatches(value, entry))
                {
                    return true;
                }
            }
            return false;
        }
        if (value is null || value == DBNull.Value)
        {
            return false;
        }

        var kind = condition.GetValueKind();
        var condStr = kind == JsonValueKind.String ? condition.GetValue<string>() : condition.ToString();
        return string.Equals(value.ToString(), condStr, StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask<bool> PrepareCommand(
        NpgsqlConnection connection,
        NpgsqlCommand command,
        string commandText,
        HttpContext context,
        RoutineEndpoint endpoint,
        bool unknownResults,
        CancellationToken cancellationToken)
    {
        await OpenConnectionAsync(connection, context, endpoint, cancellationToken);
        command.CommandText = commandText;

        if (endpoint.CommandTimeout.HasValue)
        {
            command.CommandTimeout = endpoint.CommandTimeout.Value.Seconds;
        }

        if (Options.CommandCallbackAsync is not null)
        {
            await Options.CommandCallbackAsync(endpoint, command, context);
            if (context.Response.HasStarted || context.Response.StatusCode != (int)HttpStatusCode.OK)
            {
                return false;
            }
        }
        if (unknownResults)
        {
            if (endpoint.Routine.UnknownResultTypeList is not null)
            {
                command.UnknownResultTypeList = endpoint.Routine.UnknownResultTypeList;
            }
            else
            {
                command.AllResultTypesAreUnknown = true;
            }
        }
        else
        {
            command.AllResultTypesAreUnknown = false;
        }
        return true;
    }

    private async ValueTask OpenConnectionAsync(NpgsqlConnection connection, HttpContext context, RoutineEndpoint endpoint, CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            if (Options.BeforeConnectionOpen is not null)
            {
                Options.BeforeConnectionOpen(connection, endpoint, context);
            }
            await connection.OpenRetryAsync(Options.ConnectionRetryOptions, cancellationToken);
        }
    }

    private static void SearializeHeader(NpgsqlRestOptions options, HttpContext context, ref string? headers)
    {
        if (Options.CustomRequestHeaders.Count > 0)
        {
            foreach (var header in Options.CustomRequestHeaders)
            {
                context.Request.Headers.Add(header);
            }
        }

        var sb = new StringBuilder();
        sb.Append('{');
        var i = 0;

        foreach (var header in context.Request.Headers)
        {
            if (i++ > 0)
            {
                sb.Append(',');
            }
            sb.Append(SerializeString(header.Key));
            sb.Append(':');
            sb.Append(SerializeString(header.Value.ToString()));
        }
        sb.Append('}');
        headers = sb.ToString();
    }

    private static void MapProxyResponseToParameters(
        ProxyResponse proxyResponse,
        NpgsqlParameterCollection parameters,
        RoutineEndpoint endpoint)
    {
        var proxyOptions = Options.ProxyOptions;

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = (NpgsqlRestParameter)parameters[i];
            var paramName = parameter.ActualName ?? parameter.ParameterName;

            if (endpoint.ProxyResponseParameterNames?.Contains(paramName) != true)
            {
                continue;
            }

            if (string.Equals(paramName, proxyOptions.ResponseStatusCodeParameter, StringComparison.OrdinalIgnoreCase))
            {
                // Use NpgsqlDbType (which reflects @param retype) rather than TypeDescriptor
                // (which reflects the original Describe type and may be stale for SQL files)
                var dbType = parameter.NpgsqlDbType;
                if (dbType == NpgsqlTypes.NpgsqlDbType.Text || dbType == NpgsqlTypes.NpgsqlDbType.Varchar || dbType == NpgsqlTypes.NpgsqlDbType.Unknown)
                {
                    parameter.Value = proxyResponse.StatusCode.ToString();
                }
                else
                {
                    parameter.Value = proxyResponse.StatusCode;
                }
            }
            else if (string.Equals(paramName, proxyOptions.ResponseBodyParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.Body ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseHeadersParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.Headers ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseContentTypeParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.ContentType ?? DBNull.Value;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseSuccessParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = proxyResponse.IsSuccess;
            }
            else if (string.Equals(paramName, proxyOptions.ResponseErrorMessageParameter, StringComparison.OrdinalIgnoreCase))
            {
                parameter.Value = (object?)proxyResponse.ErrorMessage ?? DBNull.Value;
            }
        }
    }

    /// <summary>
    /// Check if a parameter is a proxy response parameter that will be filled in by the proxy response.
    /// </summary>
    private static bool IsProxyResponseParameter(RoutineEndpoint endpoint, NpgsqlRestParameter parameter)
    {
        if (!endpoint.IsProxy || !Options.ProxyOptions.Enabled || endpoint.ProxyResponseParameterNames is null)
        {
            return false;
        }

        var paramName = parameter.ActualName ?? parameter.ConvertedName;
        return paramName is not null && endpoint.ProxyResponseParameterNames.Contains(paramName);
    }

    private static void WriteStringBuilderToWriter(StringBuilder row, PipeWriter writer)
    {
        foreach (ReadOnlyMemory<char> chunk in row.GetChunks())
        {
            var buffer = writer.GetSpan(Encoding.UTF8.GetMaxByteCount(chunk.Length));
            int bytesWritten = Encoding.UTF8.GetBytes(chunk.Span, buffer);
            writer.Advance(bytesWritten);
        }
    }

    private async ValueTask ReturnErrorAsync(
        string message,
        bool log, HttpContext context,
        CancellationToken cancellationToken,
        int statusCode = (int)HttpStatusCode.InternalServerError)
    {
        if (log)
        {
            Logger?.LogError("{message}", message);
        }
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = Text.Plain;
        await context.Response.WriteAsync(message, cancellationToken);
        await context.Response.CompleteAsync();
    }

    private async ValueTask<bool> ValidateParametersAsync(
        NpgsqlParameterCollection parameters,
        RoutineEndpoint endpoint,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (endpoint.ParameterValidations is null)
        {
            return true;
        }

        foreach (var kvp in endpoint.ParameterValidations)
        {
            var originalParamName = kvp.Key;
            var rules = kvp.Value;

            // Find the parameter by original name
            NpgsqlRestParameter? parameter = null;
            for (var i = 0; i < parameters.Count; i++)
            {
                var p = parameters[i] as NpgsqlRestParameter;
                if (p is not null && string.Equals(p.ActualName, originalParamName, StringComparison.Ordinal))
                {
                    parameter = p;
                    break;
                }
            }

            if (parameter is null)
            {
                continue;
            }

            // Get the value to validate - use OriginalStringValue for consistency
            var valueToValidate = parameter.OriginalStringValue;
            var convertedParamName = parameter.ConvertedName;

            // Find rule names for error messages
            foreach (var rule in rules)
            {
                // Find the rule name by looking it up in ValidationOptions
                var ruleName = FindRuleName(rule);

                var (isValid, message) = ValidateParameterValue(
                    valueToValidate,
                    parameter.Value,
                    rule,
                    originalParamName,
                    convertedParamName,
                    ruleName);

                if (!isValid)
                {
                    var urlInfo = string.Concat(endpoint.Method.ToString(), " ", endpoint.Path);
                    Logger?.ValidationFailed(urlInfo, originalParamName, message);

                    context.Response.StatusCode = rule.StatusCode;
                    context.Response.ContentType = Text.Plain;
                    await context.Response.WriteAsync(message, cancellationToken);
                    await context.Response.CompleteAsync();
                    return false;
                }
            }
        }

        return true;
    }

    private static string FindRuleName(ValidationRule rule)
    {
        foreach (var kvp in Options.ValidationOptions.Rules)
        {
            if (ReferenceEquals(kvp.Value, rule))
            {
                return kvp.Key;
            }
        }
        return string.Empty;
    }

    private static (bool IsValid, string Message) ValidateParameterValue(
        string? originalStringValue,
        object? value,
        ValidationRule rule,
        string originalParamName,
        string convertedParamName,
        string ruleName)
    {
        // Message format: {0} = original param name, {1} = converted param name, {2} = rule name
        var message = string.Format(rule.Message, originalParamName, convertedParamName, ruleName);

        switch (rule.Type)
        {
            case ValidationType.NotNull:
                // Check if value is null or DBNull
                if (value is null || value == DBNull.Value)
                {
                    return (false, message);
                }
                break;

            case ValidationType.NotEmpty:
                // Check if value is an empty string (null values pass - use NotNull for null check)
                if (originalStringValue is not null && originalStringValue.Length == 0)
                {
                    return (false, message);
                }
                break;

            case ValidationType.Required:
                // Combines NotNull and NotEmpty - value cannot be null or empty string
                if (value is null || value == DBNull.Value)
                {
                    return (false, message);
                }
                if (originalStringValue is not null && originalStringValue.Length == 0)
                {
                    return (false, message);
                }
                break;

            case ValidationType.Regex:
                if (rule.Pattern is null)
                {
                    return (true, string.Empty);
                }
                // Null/empty values don't match regex (use NotEmpty rule first if required)
                if (string.IsNullOrEmpty(originalStringValue))
                {
                    return (false, message);
                }
                if (!System.Text.RegularExpressions.Regex.IsMatch(originalStringValue, rule.Pattern))
                {
                    return (false, message);
                }
                break;

            case ValidationType.MinLength:
                if (rule.MinLength is null)
                {
                    return (true, string.Empty);
                }
                var strValue = originalStringValue ?? string.Empty;
                if (strValue.Length < rule.MinLength.Value)
                {
                    return (false, message);
                }
                break;

            case ValidationType.MaxLength:
                if (rule.MaxLength is null)
                {
                    return (true, string.Empty);
                }
                var strVal = originalStringValue ?? string.Empty;
                if (strVal.Length > rule.MaxLength.Value)
                {
                    return (false, message);
                }
                break;
        }

        return (true, string.Empty);
    }
}
