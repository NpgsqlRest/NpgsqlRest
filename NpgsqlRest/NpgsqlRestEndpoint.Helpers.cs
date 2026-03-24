using System.Data;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using Npgsql;
using NpgsqlRest.Proxy;
using static System.Net.Mime.MediaTypeNames;

namespace NpgsqlRest;

public partial class NpgsqlRestEndpoint
{
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
                if (parameter.TypeDescriptor.IsText)
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
