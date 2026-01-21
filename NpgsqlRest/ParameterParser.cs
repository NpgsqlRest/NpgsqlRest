using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using NpgsqlTypes;

namespace NpgsqlRest;

internal static class ParameterParser
{
    internal static bool TryParseParameter(
        NpgsqlRestParameter parameter,
        ref StringValues values,
        QueryStringNullHandling queryStringNullHandling)
    {
        if (parameter.TypeDescriptor.IsArray == false)
        {
            if (values.Count == 0)
            {
                parameter.Value = DBNull.Value;
                return true;
            }
            if (values.Count == 1)
            {
                var value = values[0];
                if (TryGetValue(value, out var resultValue))
                {
                    parameter.Value = resultValue;
                    parameter.OriginalStringValue = value;
                    return true;
                }
                return false;
            }
            else
            {
                StringBuilder sb = new();
                for (var i = 0; i < values.Count; i++)
                {
                    sb.Append(values[i]);
                }
                var value = sb.ToString();
                if (TryGetValue(value, out var resultValue))
                {
                    parameter.Value = resultValue;
                    parameter.OriginalStringValue = value;
                    return true;
                }
                return false;
            }
        }

        if (values.Count == 0)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        var list = new List<object?>(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            if (TryGetValue(value, out var resultValue))
            {
                list.Add(resultValue);
            }
            else
            {
                return false;
            }
        }
        parameter.Value = list;
        // For arrays, store the original values as a joined string
        parameter.OriginalStringValue = string.Join(",", values.ToArray());
        return true;

        bool TryGetValue(
            string? value,
            out object? resultValue)
        {
            if (queryStringNullHandling == QueryStringNullHandling.NullLiteral)
            {
                if (string.Equals(value, Consts.Null, StringComparison.OrdinalIgnoreCase))
                {
                    resultValue = DBNull.Value;
                    return true;
                }
            }
            else if (queryStringNullHandling == QueryStringNullHandling.EmptyString)
            {
                if (string.IsNullOrEmpty(value))
                {
                    resultValue = DBNull.Value;
                    return true;
                }
            }

            // Fast path for text types
            if (parameter.TypeDescriptor.IsText)
            {
                resultValue = value;
                return true;
            }

            // Empty check for non-text types
            if (string.IsNullOrEmpty(value))
            {
                resultValue = DBNull.Value;
                return true;
            }

            // Use delegate lookup for type-specific parsing
            var parser = ParameterParsers.GetParser(parameter.TypeDescriptor.BaseDbType);
            if (parser is not null)
            {
                return parser(value, out resultValue);
            }

            // Fallback for unknown types - use raw string
            resultValue = value;
            return true;
        }
    }

    internal static bool TryParseParameter(
        NpgsqlRestParameter parameter,
        JsonNode? value)
    {
        if (value is null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        JsonValueKind kind = value.GetValueKind();
        if (kind == JsonValueKind.Null)
        {
            parameter.Value = DBNull.Value;
            return true;
        }

        if (TryGetNonStringValue(value, ref kind, out var nonStringValue))
        {
            parameter.Value = nonStringValue;
            // Store the original JSON representation for cache key consistency
            parameter.OriginalStringValue = value.ToJsonString();
            return true;
        }

        if (kind == JsonValueKind.Array)
        {
            var list = new List<object?>();
            JsonArray array = value.AsArray();
            for (var i = 0; i < array.Count; i++)
            {
                var arrayItem = array[i];
                if (arrayItem is null)
                {
                    list.Add(null);
                    continue;
                }
                var arrayItemKind = arrayItem.GetValueKind();
                if (arrayItemKind == JsonValueKind.Null)
                {
                    list.Add(null);
                    continue;
                }
                if (TryGetNonStringValue(arrayItem, ref arrayItemKind, out object arrayValue))
                {
                    list.Add(arrayValue);
                    continue;
                }

                var arrayItemContent = arrayItem.ToString();
                if (TryGetNonStringValueFromString(arrayItemContent, out arrayValue))
                {
                    list.Add(arrayValue);
                    continue;
                }
                list.Add(arrayItemContent);
            }
            parameter.Value = list;
            // Store the original JSON array representation for cache key consistency
            parameter.OriginalStringValue = value.ToJsonString();
            return true;
        }

        var content = value.ToString();
        if (TryGetNonStringValueFromString(content, out nonStringValue))
        {
            parameter.Value = nonStringValue;
            parameter.OriginalStringValue = content;
            return true;
        }

        parameter.Value = content;
        parameter.OriginalStringValue = content;
        return true;

        bool TryGetNonStringValue(
            JsonNode value, 
            ref JsonValueKind valueKind, 
            out object result)
        {
            try
            {
                if (valueKind == JsonValueKind.Number)
                {
                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Smallint)
                    {
                        result = value.GetValue<short>();
                        return true;
                    }

                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Integer)
                    {
                        result = value.GetValue<int>();
                        return true;
                    }

                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Bigint)
                    {
                        result = value.GetValue<long>();
                        return true;
                    }

                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Double)
                    {
                        result = value.GetValue<double>();
                        return true;
                    }

                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Real)
                    {
                        result = value.GetValue<float>();
                        return true;
                    }

                    if (parameter.TypeDescriptor.BaseDbType is NpgsqlDbType.Numeric or NpgsqlDbType.Money)
                    {
                        result = value.GetValue<decimal>();
                        return true;
                    }
                }
                if (valueKind is JsonValueKind.True or JsonValueKind.False && parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Boolean)
                {
                    result = value.GetValue<bool>();
                    return true;
                }
            }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException)
            {
            }
            result = null!;
            return false;
        }

        bool TryGetNonStringValueFromString(
            string nonStrContent,
            out object result)
        {
            // Use delegate lookup for type-specific parsing
            var parser = ParameterParsers.GetParser(parameter.TypeDescriptor.BaseDbType);
            if (parser is not null && parser(nonStrContent, out var parsed) && parsed is not null)
            {
                result = parsed;
                return true;
            }
            result = null!;
            return false;
        }
    }

    internal static string FormatParameterForLog(NpgsqlRestParameter parameter)
    {
        var value = parameter.NpgsqlValue;
        var descriptor = parameter.TypeDescriptor;

        if (value is null || value == DBNull.Value)
        {
            return Consts.Null;
        }

        // Use OriginalStringValue if available and format it according to PostgreSQL standard
        if (parameter.OriginalStringValue is not null)
        {
            if (descriptor.IsArray)
            {
                // Format as PostgreSQL array literal: '{value1,value2}'
                return string.Concat("'", parameter.OriginalStringValue, "'");
            }
            if (descriptor is { IsNumeric: false, IsBoolean: false })
            {
                return string.Concat("'", parameter.OriginalStringValue, "'");
            }
            return parameter.OriginalStringValue;
        }

        // Fallback for internally-set values (user claims, IP address, etc.)
        if (descriptor.IsArray)
        {
            if (value is IList<object> objectList)
            {
                var d = descriptor;
                if (descriptor is { IsNumeric: false, IsBoolean: false })
                {
                    return string.Concat("'{", string.Join(",", objectList.Select(x => Format(x, d))), "}'");
                }
                return string.Concat("'{", string.Join(",", objectList.Select(x => Format(x, d))), "}'");
            }
            if (value is IList<string> stringList)
            {
                var d = descriptor;
                if (descriptor is { IsNumeric: false, IsBoolean: false })
                {
                    return string.Concat("'{", string.Join(",", stringList.Select(x => Format(x, d))), "}'");
                }
                return string.Concat("'{", string.Join(",", stringList.Select(x => Format(x, d))), "}'");
            }
        }
        if (descriptor is { IsNumeric: false, IsBoolean: false })
        {
            return string.Concat("'", Format(value, descriptor), "'");
        }

        return Format(value, descriptor);

        static string Format(object v, TypeDescriptor descriptor)
        {
            if (v is DateTime dt)
            {
                if (descriptor.BaseDbType == NpgsqlDbType.TimestampTz)
                {
                    return dt.ToString("O");
                }
                return dt.ToString("s");
            }
            if (v is DateOnly dateOnly)
            {
                return dateOnly.ToString("O");
            }
            if (v is DateTimeOffset dto)
            {
                return dto.DateTime.ToString("T");
            }

            if (descriptor.IsBoolean)
            {
                return v.ToString()?.ToLowerInvariant() ?? string.Empty;
            }
            return v.ToString() ?? string.Empty;
        }
    }
}
