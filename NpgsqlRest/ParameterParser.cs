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

            if (!parameter.TypeDescriptor.IsText && string.IsNullOrEmpty(value))
            {
                resultValue = DBNull.Value;
                return true;
            }

            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Smallint)
            {
                if (short.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var shortValue))
                {
                    resultValue = shortValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Integer)
            {
                if (int.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var intValue))
                {
                    resultValue = intValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Bigint)
            {
                if (long.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var bigValue))
                {
                    resultValue = bigValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Double)
            {
                if (double.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var doubleValue))
                {
                    resultValue = doubleValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Real)
            {
                if (float.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var floatValue))
                {
                    resultValue = floatValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Numeric || parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Money)
            {
                if (decimal.TryParse(value, CultureInfo.InvariantCulture.NumberFormat, out var decimalValue))
                {
                    resultValue = decimalValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Boolean)
            {
                if (bool.TryParse(value, out var boolValue))
                {
                    resultValue = boolValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Timestamp || parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimestampTz)
            {
                if (DateTime.TryParse(value, out var dateTimeValue))
                {
                    resultValue = parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimestampTz ? DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc) : dateTimeValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Date)
            {
                if (DateOnly.TryParse(value, out var dateTimeValue))
                {
                    resultValue = dateTimeValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }

            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Time || parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimeTz)
            {
                if (DateTime.TryParse(value, out var dateTimeValue))
                {
                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimeTz)
                    {
                        resultValue = new DateTimeOffset(DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc));
                    }
                    else
                    {
                        resultValue = TimeOnly.FromDateTime(dateTimeValue);
                    }
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Uuid)
            {
                if (Guid.TryParse(value, out var guidValue))
                {
                    resultValue = guidValue;
                    return true;
                }
                resultValue = null!;
                return false;
            }
            if (parameter.TypeDescriptor.IsText)
            {
                resultValue = value;
                return true;
            }

            // for all other cases, use raw string
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
            if (parameter.TypeDescriptor.BaseDbType is NpgsqlDbType.Timestamp or NpgsqlDbType.TimestampTz)
            {
                if (DateTime.TryParse(nonStrContent, out var dateTimeValue))
                {
                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimestampTz)
                    {
                        result = DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc);
                    }
                    else
                    {
                        result = dateTimeValue;
                    }
                    return true;
                }
            }
            else if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Date)
            {
                if (DateOnly.TryParse(nonStrContent, out var dateTimeValue))
                {
                    result = dateTimeValue;
                    return true;
                }
            }
            else if (parameter.TypeDescriptor.BaseDbType is NpgsqlDbType.Time or NpgsqlDbType.TimeTz)
            {
                if (DateTime.TryParse(nonStrContent, out var dateTimeValue))
                {
                    if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.TimeTz)
                    {
                        result = new DateTimeOffset(DateTime.SpecifyKind(dateTimeValue, DateTimeKind.Utc));
                    }
                    else
                    {
                        result = TimeOnly.FromDateTime(dateTimeValue);
                    };
                    return true;
                }
            }
            else if (parameter.TypeDescriptor.BaseDbType == NpgsqlDbType.Uuid)
            {
                if (Guid.TryParse(nonStrContent, out var guidValue))
                {
                    result = guidValue;
                    return true;
                }
            }
            result = null!;
            return false;
        }
    }

    internal static string FormatParam(object value, TypeDescriptor descriptor)
    {
        if (value == DBNull.Value)
        {
            return Consts.Null;
        }
        if (descriptor.IsArray)
        {
            if (value is IList<object> objectList)
            {
                var d = descriptor;
                if (descriptor is { IsNumeric: false, IsBoolean: false })
                {
                    return string.Concat("{", string.Join(",", objectList.Select(x => string.Concat("'", Format(x, d), "'"))),
                        "}");
                }
                return string.Concat("{", string.Join(",", objectList.Select(x => Format(x, d))), "}");
            }
            if (value is IList<string> stringList)
            {
                var d = descriptor;
                if (descriptor is { IsNumeric: false, IsBoolean: false })
                {
                    return string.Concat("{", string.Join(",", stringList.Select(x => string.Concat("'", Format(x, d), "'"))),
                        "}");
                }
                return string.Concat("{", string.Join(",", stringList.Select(x => Format(x, d))), "}");
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
